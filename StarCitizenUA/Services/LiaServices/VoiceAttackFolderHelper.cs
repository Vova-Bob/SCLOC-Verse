using StarCitizenUA.Interfaces;
using System.Collections.Concurrent;
using System.IO;
using System.Windows.Controls;

namespace StarCitizenUA.Services.LiaServices
{
    class VoiceAttackFolderHelper : IVoiceAttackFolderHelper
    {
        private readonly IToastService _toastService;

        public VoiceAttackFolderHelper(IToastService toastService)
        {
            _toastService = toastService;
        }

        private static readonly HashSet<string> IgnoredDirs = new(StringComparer.OrdinalIgnoreCase)
        {
            "$Recycle.Bin",
            "System Volume Information",
            "Config.Msi",
            "Windows",
            "ProgramData",
            "Recovery",
            "MSOCache"
        };

        public async Task<string?> FindVoiceAttackImportFolderAsync()
        {
            // 1️⃣ Спочатку перевіряємо типові місця
            var possiblePaths = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VoiceAttack 2", "Apps", "Import"),
                @"C:\Program Files (x86)\VoiceAttack\Apps\Import",
                @"C:\Program Files\VoiceAttack\Apps\Import",
                @"C:\Program Files (x86)\SteamLibrary\steamapps\common\VoiceAttack 2\Apps\Import"
            };

            foreach (var path in possiblePaths)
            {
                if (Directory.Exists(path))
                    return path;
            }

            // 2️⃣ Якщо не знайдено — шукаємо на дисках
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                .ToArray();

            using var cts = new CancellationTokenSource();
            var tasks = drives.Select(d =>
                Task.Run(() => SearchVoiceAttackParallel(d.RootDirectory.FullName, "VoiceAttack 2", 5, cts.Token))
            ).ToArray();

            var completed = await Task.WhenAny(tasks);
            var result = completed.Result;

            // Відміняємо решту пошуків
            cts.Cancel();

            if (!string.IsNullOrEmpty(result))
            {
                var importPath = Path.Combine(result, "Apps", "Import");
                return Directory.Exists(importPath) ? importPath : null;
            }

            return null;
        }

        private string? SearchVoiceAttackParallel(string rootDir, string targetFolder, int maxDepth, CancellationToken token)
        {
            var queue = new ConcurrentQueue<(string dir, int depth)>();
            queue.Enqueue((rootDir, 0));
            string? foundPath = null;

            Parallel.ForEach(
                Partitioner.Create(Enumerable.Range(0, Environment.ProcessorCount)), // баланс потоків
                new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = token },
                _ =>
                {
                    while (!queue.IsEmpty && !token.IsCancellationRequested)
                    {
                        if (!queue.TryDequeue(out var item)) continue;
                        if (item.depth > maxDepth) continue;

                        string[] subdirs;
                        try
                        {
                            subdirs = Directory.GetDirectories(item.dir);
                        }
                        catch
                        {
                            continue;
                        }

                        foreach (var dir in subdirs)
                        {
                            var dirName = Path.GetFileName(dir);
                            if (IgnoredDirs.Contains(dirName)) continue;

                            // Якщо знайдено "VoiceAttack 2"
                            if (dirName.Equals(targetFolder, StringComparison.OrdinalIgnoreCase))
                            {
                                var importPath = Path.Combine(dir, "Apps", "Import");
                                if (Directory.Exists(importPath))
                                {
                                    foundPath = dir;
                                    token.ThrowIfCancellationRequested();
                                    return;
                                }
                            }

                            queue.Enqueue((dir, item.depth + 1));
                        }
                    }
                });

            return foundPath;
        }

        // Основний метод — отримати або знайти папку VoiceAttack і показати користувачу.
        public async Task<string?> GetOrPromptVoiceAttackFolderAsync(TextBox folderDisplayControl)
        {
            string? savedFolder = Settings.Default.StarCitizenLIA;

            // 1️⃣ Якщо збережена і валідна
            if (!string.IsNullOrEmpty(savedFolder) && Directory.Exists(savedFolder))
            {
                folderDisplayControl.Text = savedFolder;
                return savedFolder;
            }

            // 2️⃣ Якщо ні — шукаємо автоматично
            var foundFolder = await FindVoiceAttackImportFolderAsync();

            if (!string.IsNullOrEmpty(foundFolder))
            {
                Settings.Default.StarCitizenLIA = foundFolder;
                Settings.Default.Save();

                folderDisplayControl.Text = foundFolder;
                return foundFolder;
            }

            // 3️⃣ Якщо не знайдено — повідомлення користувачу
            _toastService.ShowToast("Не вдалося знайти папку VoiceAttack. Будь ласка, оберіть вручну.");
            return null;
        }
    }
}
