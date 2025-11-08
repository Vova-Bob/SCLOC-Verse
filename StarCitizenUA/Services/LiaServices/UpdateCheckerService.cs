using StarCitizenUA.Interfaces;
using System.IO;
using System.Net.Http;

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

            // Отримання локальної версії
            string? localVersion = GetLocalVoicePackVersion(Path.GetDirectoryName(versionFilePath)!);

            // Завантаження або оновлення version.txt
            try
            {
                using HttpClient client = new();
                string remoteContent = await client.GetStringAsync(AppSettings.VersionFile);

                // Витягуємо серверну версію з remoteContent
                string? remoteVersion = remoteContent
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(l => l.StartsWith("version=", StringComparison.OrdinalIgnoreCase)
                                      || l.StartsWith("#version=", StringComparison.OrdinalIgnoreCase))
                    ?.Replace("version=", "").Replace("#", "").Trim();

                // Якщо версія новіша або локального файлу немає — оновлюємо
                if (string.IsNullOrEmpty(localVersion) || remoteVersion != localVersion)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(versionFilePath)!);
                    await File.WriteAllTextAsync(versionFilePath, remoteContent);
                    localVersion = remoteVersion; // оновлюємо локальну версію для подальшої перевірки
                }
            }
            catch
            {
                // Якщо локальної версії немає і завантажити не вдалося — повідомляємо про помилку
                if (string.IsNullOrEmpty(localVersion))
                    return ("Не вдалося завантажити файл версій.", System.Windows.Media.Brushes.Red);
                // Інакше залишаємо старий файл, бо він існує
            }

            // Пошук папки VoiceAttack
            string? voiceAttackSoundsPath = await _voiceAttackFolderHelper.FindVoiceAttackImportFolderAsync(cts.Token);
            string voicePackFolder = voiceAttackSoundsPath ?? string.Empty;

            // Якщо папка не обрана
            if (string.IsNullOrWhiteSpace(voicePackFolder))
                return ("Папка VoiceAttack не обрана!", System.Windows.Media.Brushes.Red);

            // Якщо папка обрана, але не існує на диску
            if (!Directory.Exists(voicePackFolder))
                return ("Папка VoiceAttack не існує!", System.Windows.Media.Brushes.Red);

            string[] lines;
            int expectedMinFileCount = 0;
            List<string> expectedFiles = new();

            try
            {
                // Зчитування локального version.txt
                lines = await File.ReadAllLinesAsync(versionFilePath);

                // Отримання мінімальної кількості файлів із файлу версій
                string? minFilesLine = lines.FirstOrDefault(l => l.StartsWith("minFiles=", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(minFilesLine))
                    int.TryParse(minFilesLine.Replace("minFiles=", "").Trim(), out expectedMinFileCount);

                // Збір очікуваних файлів
                int startIndex = Array.FindIndex(lines, l => l.StartsWith("minFiles=", StringComparison.OrdinalIgnoreCase)) + 1;
                for (int i = startIndex; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    string filePath = line.Split('|')[0].Trim().Replace('/', Path.DirectorySeparatorChar);
                    expectedFiles.Add(filePath);
                }
            }
            catch
            {
                return ("Помилка при зчитуванні version.txt.", System.Windows.Media.Brushes.Red);
            }

            // Підрахунок існуючих файлів і збір відсутніх
            int existingFileCount = 0;
            List<string> missingFiles = new();

            foreach (var f in expectedFiles)
            {
                // Ігноруємо Star Citizen LIA-Profile.vap
                if (f.Equals("Star Citizen LIA-Profile.vap", StringComparison.OrdinalIgnoreCase))
                {
                    existingFileCount++;
                    continue;
                }

                string fullPath = Path.Combine(voicePackFolder, f);
                if (File.Exists(fullPath))
                    existingFileCount++;
                else
                    missingFiles.Add(f);
            }

            // Якщо відсутні файли (крім ігнорованого)
            if (missingFiles.Any())
            {
                if (existingFileCount == 1)
                    return ($"Голосовий пакет не встановлено.", System.Windows.Media.Brushes.Red);

                string missing = string.Join(", ", missingFiles);
                return ($"Голосовий пакет неповний. Не знайдено файли: {missing}", System.Windows.Media.Brushes.Orange);
            }

            // Перевірка мінімальної кількості файлів
            if (expectedMinFileCount > 0 && existingFileCount < expectedMinFileCount)
                return ($"Голосовий пакет неповний: знайдено {existingFileCount} із {expectedMinFileCount} файлів.", System.Windows.Media.Brushes.Orange);

            // Перевірка версії пакету
            if (!string.IsNullOrEmpty(localVersion))
                return ($"У вас актуальна версія голосового пакету: {localVersion}", System.Windows.Media.Brushes.LimeGreen);

            return ($"Доступне оновлення голосового пакету: {localVersion}", System.Windows.Media.Brushes.Red);
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

        public string? GetLocalVoicePackVersion(string localFolder)
        {
            string versionFile = Path.Combine(localFolder, "version.txt");
            if (!File.Exists(versionFile))
                return null;

            var lines = File.ReadAllLines(versionFile);
            var line = lines.FirstOrDefault(l => l.StartsWith("version=") || l.StartsWith("#version="));
            return line?.Replace("version=", "").Replace("#", "").Trim();
        }
    }
}