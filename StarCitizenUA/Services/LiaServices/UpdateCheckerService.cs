using StarCitizenUA.Interfaces;
using System.IO;
using System.Net.Http;
using System.Threading;

namespace StarCitizenUA.Services.LiaServices
{
    public class UpdateCheckerService
    {
        private readonly IVoiceAttackFolderHelper _voiceAttackFolderHelper;
        CancellationTokenSource cts = new CancellationTokenSource();

        public UpdateCheckerService(IVoiceAttackFolderHelper voiceAttackFolderHelper)
        {
            _voiceAttackFolderHelper = voiceAttackFolderHelper;
        }

        public async Task<(string Message, System.Windows.Media.Brush Color)> CheckForPendingUpdatesAsync()
        {
            string localFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string versionFilePath = Path.Combine(localFolder, "L.I.A Voice Pack Updater", "version.txt");

            if (!File.Exists(versionFilePath))
            {
                try
                {
                    using HttpClient client = new();
                    var remoteContent = await client.GetStringAsync(AppSettings.VersionFile);

                    Directory.CreateDirectory(Path.GetDirectoryName(versionFilePath)!);
                    await File.WriteAllTextAsync(versionFilePath, remoteContent);
                }
                catch
                {
                    return ("Не вдалося завантажити файл версій.", System.Windows.Media.Brushes.Red);
                }
            }

            string? voicePackVersion = (await AppSettings.GetVoicePackVersionAsync())?.Trim();

            string? voiceAttackSoundsPath = await _voiceAttackFolderHelper.FindVoiceAttackImportFolderAsync(cts.Token);
            string voicePackFolder = voiceAttackSoundsPath ?? string.Empty;

            bool voicePackExists = Directory.Exists(voicePackFolder) &&
                                   Directory.EnumerateFiles(voicePackFolder, "*.*", SearchOption.AllDirectories).Any();

            if (!voicePackExists)
                return ("Голосовий пакет ще не встановлено!", System.Windows.Media.Brushes.Red);

            string[] lines;
            string? remoteVersion = null;
            int expectedMinFileCount = 0;

            try
            {
                lines = await File.ReadAllLinesAsync(versionFilePath);

                remoteVersion = lines.FirstOrDefault(l => l.StartsWith("version=") || l.StartsWith("#version="))?
                                     .Replace("version=", "").Replace("#", "").Trim();

                string? minFilesLine = lines.FirstOrDefault(l => l.StartsWith("minFiles=", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(minFilesLine))
                {
                    int.TryParse(minFilesLine.Replace("minFiles=", "").Trim(), out expectedMinFileCount);
                }
            }
            catch
            {
                return ("Помилка при зчитуванні version.txt.", System.Windows.Media.Brushes.Red);
            }

            //первірка на кількість файлів
            //var expectedFiles = lines
            //    .SkipWhile(l => !l.StartsWith("minFiles=", StringComparison.OrdinalIgnoreCase))
            //    .Skip(1)
            //    .Where(l => !string.IsNullOrWhiteSpace(l))
            //    .Select(l => l.Split('|')[0].Replace('/', Path.DirectorySeparatorChar))
            //    .ToList();

            //int existingFileCount = expectedFiles.Count(f => File.Exists(Path.Combine(voicePackFolder, f)));
            //if (expectedMinFileCount > 0 && existingFileCount < expectedMinFileCount)
            //    return ($"Голосовий пакет неповний: знайдено {existingFileCount} із {expectedMinFileCount} файлів.", System.Windows.Media.Brushes.Orange);

            if (!string.IsNullOrEmpty(remoteVersion) && remoteVersion == voicePackVersion)
                return ($"У вас актуальна версія голосового пакету: {voicePackVersion}", System.Windows.Media.Brushes.Green);

            return ($"Доступне оновлення голосового пакету: {voicePackVersion}", System.Windows.Media.Brushes.Red);
        }

        public async Task<bool> UpdateVersionFileAsync()
        {
            string localFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string versionFilePath = Path.Combine(localFolder, "L.I.A Voice Pack Updater", "version.txt");

            try
            {
                using HttpClient client = new();
                var remoteContent = await client.GetStringAsync(AppSettings.VersionFile);

                Directory.CreateDirectory(Path.GetDirectoryName(versionFilePath)!);
                await File.WriteAllTextAsync(versionFilePath, remoteContent);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}