using SCLOCVerse.Composition;
using SCLOCVerse.Controls.Dialogs;
using SCLOCVerse.Services.ApplicationUpdate;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Xml.Linq;

namespace SCLOCVerse
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static Mutex? _singleInstanceMutex;
        private const string SingleInstanceMutexName = "SCLOCVerse_SingleInstanceMutex";

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // РџРµСЂРµРІС–СЂРєР° single instance С‡РµСЂРµР· Р»РѕРєР°Р»СЊРЅРёР№ Mutex.
            bool createdNew;
            try
            {
                _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out createdNew);
            }
            catch (AbandonedMutexException)
            {
                // РџРѕРїРµСЂРµРґРЅС–Р№ РµРєР·РµРјРїР»СЏСЂ Р°РІР°СЂС–Р№РЅРѕ Р·Р°РІРµСЂС€РёРІСЃСЏ, mutex Р·РІС–Р»СЊРЅРµРЅРѕ.
                // РџРѕС‚РѕС‡РЅРёР№ РїСЂРѕС†РµСЃ СЃС‚Р°С” РїРµСЂС€РёРј РµРєР·РµРјРїР»СЏСЂРѕРј.
                createdNew = true;
                _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out _);
            }

            if (!createdNew)
            {
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;

                BaseDialog.Show(new DialogOptions
                {
                    Type = DialogType.Info,
                    Title = "SCLOC-Verse",
                    Message = "РџСЂРѕРіСЂР°РјР° РІР¶Рµ Р·Р°РїСѓС‰РµРЅР°. Р’Рё РјРѕР¶РµС‚Рµ РјР°С‚Рё Р»РёС€Рµ РѕРґРёРЅ Р°РєС‚РёРІРЅРёР№ РµРєР·РµРјРїР»СЏСЂ.",
                    Buttons = MessageBoxButton.OK
                });

                Shutdown();
                return;
            }

            // РђРІС‚РѕРјР°С‚РёС‡РЅР° РјС–РіСЂР°С†С–СЏ User Settings РїС–СЃР»СЏ РѕРЅРѕРІР»РµРЅРЅСЏ РІРµСЂСЃС–С—.
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

            // РЎРїСЂРѕР±Р° СЃС‚Р°РЅРґР°СЂС‚РЅРѕС— РјС–РіСЂР°С†С–С— Application Settings.
            try
            {
                Settings.Default.Upgrade();
            }
            catch
            {
                // Р†РіРЅРѕСЂСѓС”РјРѕ РїРѕРјРёР»РєРё РјС–РіСЂР°С†С–С—, С‰РѕР± РЅРµ Р±Р»РѕРєСѓРІР°С‚Рё Р·Р°РїСѓСЃРє РґРѕРґР°С‚РєР°.
            }

            // РЇРІРЅРѕ Р·Р°Р±РёСЂР°С”РјРѕ Р·РЅР°С‡РµРЅРЅСЏ Р· РїРѕРїРµСЂРµРґРЅСЊРѕС— РІРµСЂСЃС–С—.
            // РќР° РјРѕРјРµРЅС‚ РїРµСЂС€РѕРіРѕ Р·Р°РїСѓСЃРєСѓ РЅРѕРІРѕС— РІРµСЂСЃС–С— РєРѕСЂРёСЃС‚СѓРІР°С‡ С‰Рµ РЅРµ РјР°РІ РјРѕР¶Р»РёРІРѕСЃС‚С–
            // Р·РјС–РЅРёС‚Рё РЅР°Р»Р°С€С‚СѓРІР°РЅРЅСЏ, С‚РѕРјСѓ РїРѕРїРµСЂРµРґРЅС” Р·РЅР°С‡РµРЅРЅСЏ РјР°С” РїСЂС–РѕСЂРёС‚РµС‚.
            var previousValues = TryReadPreviousVersionSettings(currentVersion);

            if (!string.IsNullOrWhiteSpace(previousValues.StarCitizenUA))
            {
                Settings.Default.StarCitizenUA = previousValues.StarCitizenUA;
            }

            if (!string.IsNullOrWhiteSpace(previousValues.UpdateChannel))
            {
                Settings.Default.UpdateChannel = previousValues.UpdateChannel;
            }

            // Р“Р°СЂР°РЅС‚СѓС”РјРѕ Р·РЅР°С‡РµРЅРЅСЏ Р·Р° Р·Р°РјРѕРІС‡СѓРІР°РЅРЅСЏРј РґР»СЏ РєР°РЅР°Р»Сѓ РѕРЅРѕРІР»РµРЅСЊ.
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
                var settingsBaseDir = Path.Combine(localAppData, "SCLOCVerse");
                if (!Directory.Exists(settingsBaseDir))
                    return (null, null);

                var urlDirs = Directory.GetDirectories(settingsBaseDir, "SCLOCVerse_Url_*");
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
                // Р†РіРЅРѕСЂСѓС”РјРѕ РїРѕРјРёР»РєРё РїСЂРё Р·РІС–Р»СЊРЅРµРЅРЅС– mutex.
            }

            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
