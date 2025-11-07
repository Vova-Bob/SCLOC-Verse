using StarCitizenUA.Interfaces;
using StarCitizenUA.Models.LiaModels;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace StarCitizenUA.Services.LiaServices
{
    public class Updater : IUpdater
    {
        private static readonly HttpClient client = new HttpClient();

        public async Task<Dictionary<string, string>> GetRemoteFileListAsync()
        {
            var content = await client.GetStringAsync(AppSettings.VersionFile);

            var files = new Dictionary<string, string>();
            foreach (var line in content.Split('\n'))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("version=")) continue;

                var parts = trimmed.Split('|');
                if (parts.Length == 2)
                    files[parts[0]] = parts[1];
            }
            return files;
        }
        
        public async Task<SyncResult> SyncFilesAsync(Dictionary<string, string> remoteFiles, string localPath, Action<string>? onProgress = null, IProgress<int>? progress = null)
        {
            var result = new SyncResult();
            string targetFolder = Path.Combine(localPath);

            int current = 0;

            foreach (var file in remoteFiles)
            {
                string localFile = Path.Combine(targetFolder, file.Key.Replace('/', Path.DirectorySeparatorChar));

                bool fileExists = File.Exists(localFile);
                bool hashMismatch = fileExists && GetHash(localFile) != file.Value;
                bool needsDownload = !fileExists || hashMismatch;

                if (needsDownload)
                {
                    if (fileExists)
                    {
                        File.Delete(localFile);
                        result.DeletedCount++;
                        onProgress?.Invoke($"🗑 Видалено старий файл: {file.Key}");
                    }

                    onProgress?.Invoke($"📥 Завантаження: {file.Key}");
                    await DownloadFileAsync(AppSettings.BaseUrl + file.Key, localFile);
                    result.Downloaded.Add(file.Key);
                    onProgress?.Invoke($"✔️ Актуально: {file.Key}");
                }
                else
                {
                    onProgress?.Invoke($"✔️ Актуально: {file.Key}");
                }

                current++;
                progress?.Report(current);
            }
        
            if (Directory.Exists(targetFolder))
            {
                var localFiles = Directory.GetFiles(targetFolder, "*", SearchOption.AllDirectories);
                foreach (var localFile in localFiles)
                {
                    string relativePath = Path.GetRelativePath(targetFolder, localFile).Replace('\\', '/');

                    if (remoteFiles.ContainsKey(relativePath) && !File.Exists(localFile))
                    {
                        File.Delete(localFile);
                        result.DeletedCount++;
                        onProgress?.Invoke($"🗑 Видалено застарілий файл: {relativePath}");
                    }
                }

                RemoveEmptyDirs(targetFolder);
            }

            return result;
        }

        public string GetHash(string filePath)
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            return BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private async Task DownloadFileAsync(string url, string localPath)
        {
            var bytes = await client.GetByteArrayAsync(url);
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            await File.WriteAllBytesAsync(localPath, bytes);
        }

        private void RemoveEmptyDirs(string root)
        {
            foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
        }

        public async Task<bool> DownloadAndInstallVoskModelAsync(string baseFolder, Action<string>? onProgress = null)
        {
            string zipPath = Path.Combine(baseFolder, AppSettings.VoskModelFileName);
            string targetPath = Path.Combine(baseFolder, AppSettings.VoskTargetFolderName);
            string manifestPath = Path.Combine(targetPath, "manifest.txt");

            if (Directory.Exists(targetPath))
            {
                if (File.Exists(manifestPath))
                {
                    var requiredFiles = await File.ReadAllLinesAsync(manifestPath);
                    bool allFilesExist = requiredFiles.All(f => File.Exists(Path.Combine(targetPath, f)));

                    if (allFilesExist)
                    {
                        onProgress?.Invoke($"✔️ Актуально: Vosk вже встановлено.");
                        return false;
                    }
                    else
                    {
                        onProgress?.Invoke("⚠️ Пошкоджена або неповна модель. Видаляємо стару папку...");
                        Directory.Delete(targetPath, true);
                    }
                }
                else
                {
                    var anyFiles = Directory.EnumerateFiles(targetPath, "*.*", SearchOption.AllDirectories).Any();
                    if (anyFiles)
                    {
                        onProgress?.Invoke("⚠️ Відсутній файл manifest.txt. Видаляємо стару папку для безпеки...");
                        Directory.Delete(targetPath, true);
                    }
                    else
                    {
                        Directory.Delete(targetPath, true);
                    }
                }
            }

            try
            {
                onProgress?.Invoke("📥 Завантаження моделі Vosk...");
                //using HttpClient client = new();
                var bytes = await client.GetByteArrayAsync(AppSettings.VoskModelUrl);
                await File.WriteAllBytesAsync(zipPath, bytes);
                onProgress?.Invoke("✔️ Завантажено архів Vosk");

                string tempExtractFolder = Path.Combine(baseFolder, "tmp_vosk_extract");
                if (Directory.Exists(tempExtractFolder))
                    Directory.Delete(tempExtractFolder, true);

                ZipFile.ExtractToDirectory(zipPath, tempExtractFolder);
                onProgress?.Invoke("📦 Розпаковано модель Vosk");

                var extractedRoot = Directory.GetDirectories(tempExtractFolder).FirstOrDefault();
                if (extractedRoot == null)
                {
                    onProgress?.Invoke("❌ Не вдалося знайти кореневу папку після розпакування");
                    return false;
                }

                var extractedFiles = Directory.GetFiles(extractedRoot, "*.*", SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(extractedRoot, f))
                    .ToList();

                Directory.Move(extractedRoot, targetPath);
                Directory.Delete(tempExtractFolder, true);
                File.Delete(zipPath);

                await File.WriteAllLinesAsync(manifestPath, extractedFiles);

                onProgress?.Invoke($"✔️ Модель Vosk встановлено.");
                onProgress?.Invoke($"🔚 Голосовий асистент Л.І.А успішно встановлений.");
                return true;
            }
            catch (Exception ex)
            {
                onProgress?.Invoke($"❌ Помилка при встановленні Vosk: {ex.Message}.");
                return false;
            }
        }
    }
}