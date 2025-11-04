using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace StarCitizenUA.Services.LiaServices
{
    public static class AppUpdateChecker
    {
        public static void EnsureCurrentVersionSaved()
        {
            string currentAppVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

            if (Settings.Default.LastAppVersion != currentAppVersion)
            {
                Settings.Default.LastAppVersion = currentAppVersion;
                Settings.Default.Save();
            }
        }

        public static async Task CheckForAppUpdateAsync()
        {
            var remoteVersion = await AppSettings.GetLatestReleaseVersionAsync();

            if (string.IsNullOrEmpty(remoteVersion))
            {
                System.Windows.MessageBox.Show("Не вдалося перевірити актуальність версії. Спробуйте пізніше.");
                return;
            }

            if (remoteVersion != Settings.Default.LastAppVersion)
            {
                var result = System.Windows.MessageBox.Show(
                    $"Доступна нова версія програми ({remoteVersion}). Бажаєте завантажити оновлення?",
                    "Доступне оновлення програми", MessageBoxButton.YesNo);

                if (result != MessageBoxResult.Yes) return;

                using var dialog = new System.Windows.Forms.FolderBrowserDialog();
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

                string selectedPath = dialog.SelectedPath;
                string zipUrl = await AppSettings.GetDownloadUrlAsync();

                if (string.IsNullOrEmpty(zipUrl))
                {
                    System.Windows.MessageBox.Show("Не вдалося знайти посилання для завантаження оновлення.");
                    return;
                }

                string zipFileName = $"L.I.A VoicePack Updater_{remoteVersion}.zip";
                string localZipPath = Path.Combine(selectedPath, zipFileName);

                try
                {
                    using HttpClient client = new HttpClient();
                    var data = await client.GetByteArrayAsync(zipUrl);
                    await File.WriteAllBytesAsync(localZipPath, data);

                    System.Windows.MessageBox.Show($"Оновлення завантажено в:\n{localZipPath}");
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show("Помилка при завантаженні: " + ex.Message);
                }
            }
        }

        public static async Task SetVersionInfoAsync(TextBlock targetTextBlock)
        {
            var remoteVersion = await AppSettings.GetLatestReleaseVersionAsync();
            var localVersion = Settings.Default.LastAppVersion;

            targetTextBlock.Inlines.Clear();
            targetTextBlock.Inlines.Add(new Run("створено @Alexuß | "));

            if (!string.IsNullOrEmpty(remoteVersion))
            {
                if (remoteVersion == localVersion)
                {
                    targetTextBlock.Inlines.Add(new Run($"Версія: {remoteVersion}"));
                }
                else
                {
                    var localRun = new Run($"Локальна версія: {localVersion} ")
                    {
                        Foreground = System.Windows.Media.Brushes.Red
                    };
                    var remoteRun = new Run($"Доступне оновлення: {remoteVersion}")
                    {
                        Foreground = System.Windows.Media.Brushes.Green
                    };

                    targetTextBlock.Inlines.Add(localRun);
                    targetTextBlock.Inlines.Add(new LineBreak());
                    targetTextBlock.Inlines.Add(remoteRun);
                }
            }
            else
            {
                targetTextBlock.Inlines.Add(new Run($"Локальна версія: {localVersion}"));
            }
        }
    }
}