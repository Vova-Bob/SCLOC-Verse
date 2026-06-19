using StarCitizenUA.Composition;
using System.Windows;

namespace StarCitizenUA
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

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
    }

}
