using StarCitizenUA.Composition;
using System.Threading;
using System.Windows;

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

            // Явна міграція налаштувань після оновлення версії додатка.
            if (Settings.Default.UpgradeRequired)
            {
                try
                {
                    Settings.Default.Upgrade();
                }
                catch
                {
                    // Ігноруємо помилки міграції, щоб не блокувати запуск додатка.
                }

                if (string.IsNullOrWhiteSpace(Settings.Default.UpdateChannel))
                {
                    Settings.Default.UpdateChannel = "Stable";
                }

                Settings.Default.UpgradeRequired = false;
                Settings.Default.Save();
            }

            var compositionRoot = new AppCompositionRoot();
            var window = compositionRoot.CreateMainWindow();
            MainWindow = window;
            window.Show();
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
