using StarCitizenUA.Composition;
using StarCitizenUA.Services.ApplicationUpdate;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Xml.Linq;

namespace StarCitizenUA
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static Mutex? _singleInstanceMutex;
        private const string SingleInstanceMutexName = "SCLocalizationUA_SingleInstanceMutex";

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Перевірка single instance через локальний Mutex.
            bool createdNew;
            try
            {
                _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out createdNew);
            }
            catch (AbandonedMutexException)
            {
                // Попередній екземпляр аварійно завершився, mutex звільнено.
                // Поточний процес стає першим екземпляром.
                createdNew = true;
                _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out _);
            }

            if (!createdNew)
            {
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;

                MessageBox.Show(
                    "Програма вже запущена. Ви можете мати лише один активний екземпляр.",
                    "SCLocalizationUA",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                Shutdown();
                return;
            }

            // Автоматична міграція User Settings після оновлення версії.
            MigrateSettingsIfNeeded();

            var compositionRoot = new AppCompositionRoot();
            var window = compositionRoot.CreateMainWindow();
            MainWindow = window;
            window.Show();
        }

        private static void MigrateSettingsIfNeeded()
        {
            var currentVersion = GetCurrentVersionString();
            var lastAppVersion = Settings.Default.LastAppVersion;

            if (string.Equals(lastAppVersion, currentVersion, StringComparison.OrdinalIgnoreCase))
                return;

            // Спроба стандартної міграції Application Settings.
            try
            {
                Settings.Default.Upgrade();
            }
            catch
            {
                // Ігноруємо помилки міграції, щоб не блокувати запуск додатка.
            }

            // Явно забираємо значення з попередньої версії.
            // На момент першого запуску нової версії користувач ще не мав можливості
            // змінити налаштування, тому попереднє значення має пріоритет.
            var previousValues = TryReadPreviousVersionSettings(currentVersion);

            if (!string.IsNullOrWhiteSpace(previousValues.StarCitizenUA))
            {
                Settings.Default.StarCitizenUA = previousValues.StarCitizenUA;
            }

            if (!string.IsNullOrWhiteSpace(previousValues.UpdateChannel))
            {
                Settings.Default.UpdateChannel = previousValues.UpdateChannel;
            }

            // Гарантуємо значення за замовчуванням для каналу оновлень.
            if (string.IsNullOrWhiteSpace(Settings.Default.UpdateChannel))
            {
                Settings.Default.UpdateChannel = "Stable";
            }

            Settings.Default.LastAppVersion = currentVersion;
            Settings.Default.UpgradeRequired = false;
            Settings.Default.Save();
        }

        private static (string? StarCitizenUA, string? UpdateChannel) TryReadPreviousVersionSettings(string currentVersion)
        {
            try
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var settingsBaseDir = Path.Combine(localAppData, "StarCitizenUA");
                if (!Directory.Exists(settingsBaseDir))
                    return (null, null);

                var urlDirs = Directory.GetDirectories(settingsBaseDir, "StarCitizenUA_Url_*");
                if (urlDirs.Length == 0)
                    return (null, null);

                Version? current = null;
                _ = Version.TryParse(currentVersion, out current);

                string? bestDir = null;
                Version? bestVersion = null;

                foreach (var urlDir in urlDirs)
                {
                    foreach (var versionDir in Directory.GetDirectories(urlDir))
                    {
                        var dirName = Path.GetFileName(versionDir);
                        if (!Version.TryParse(dirName, out var version))
                            continue;

                        if (current != null && version >= current)
                            continue;

                        if (bestVersion == null || version > bestVersion)
                        {
                            bestVersion = version;
                            bestDir = versionDir;
                        }
                    }
                }

                if (bestDir == null)
                    return (null, null);

                var configPath = Path.Combine(bestDir, "user.config");
                if (!File.Exists(configPath))
                    return (null, null);

                var doc = XDocument.Load(configPath);
                var starCitizenPath = doc.Descendants("setting")
                    .Where(s => (string?)s.Attribute("name") == "StarCitizenUA")
                    .Select(s => (string?)s.Element("value"))
                    .FirstOrDefault();
                var updateChannel = doc.Descendants("setting")
                    .Where(s => (string?)s.Attribute("name") == "UpdateChannel")
                    .Select(s => (string?)s.Element("value"))
                    .FirstOrDefault();

                return (starCitizenPath, updateChannel);
            }
            catch
            {
                return (null, null);
            }
        }

        private static string GetCurrentVersionString()
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(App).Assembly;
            var version = assembly.GetName().Version;

            return version?.ToString() ?? new Version(0, 0, 0, 0).ToString();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _singleInstanceMutex?.ReleaseMutex();
            }
            catch
            {
                // Ігноруємо помилки при звільненні mutex.
            }

            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
