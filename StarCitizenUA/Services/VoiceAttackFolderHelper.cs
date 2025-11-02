using StarCitizenUA.Interfaces;
using System.IO;

namespace StarCitizenUA.Services
{
    class VoiceAttackFolderHelper : IVoiceAttackFolderHelper
    {
        private IToastService _toastService;
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

            var tasks = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => SearchDirectoryAsync(d.RootDirectory.FullName, "VoiceAttack 2", 4))
                .ToArray();

            var results = await Task.WhenAll(tasks);

            var found = results.FirstOrDefault(r => !string.IsNullOrEmpty(r));
            return !string.IsNullOrEmpty(found) ? Path.Combine(found!, "Apps", "Import") : null;
        }

        public async Task<string?> SearchDirectoryAsync(string currentDir, string targetFolder, int maxDepth, int currentDepth = 0)
        {
            if (currentDepth > maxDepth)
                return null;

            try
            {
                var subdirs = await Task.Run(() =>
                {
                    try
                    {
                        return Directory.GetDirectories(currentDir)
                            .Where(d =>
                            {
                                var dirName = Path.GetFileName(d);
                                return !string.IsNullOrEmpty(dirName) &&
                                       !IgnoredDirs.Contains(dirName, StringComparer.OrdinalIgnoreCase);
                            })
                            .ToArray();
                    }
                    catch
                    {
                        return Array.Empty<string>();
                    }
                });

                foreach (var dir in subdirs)
                {
                    try
                    {
                        if (Path.GetFileName(dir).Equals(targetFolder, StringComparison.OrdinalIgnoreCase))
                        {
                            var importPath = Path.Combine(dir, "Apps", "Import");
                            if (Directory.Exists(importPath))
                                return dir;
                        }

                        var found = await SearchDirectoryAsync(dir, targetFolder, maxDepth, currentDepth + 1);
                        if (!string.IsNullOrEmpty(found))
                            return found;
                    }
                    catch (UnauthorizedAccessException) { continue; }
                    catch (PathTooLongException) { continue; }
                    catch { continue; }
                }
            }
            catch { }

            return null;
        }

        public async Task<string?> GetOrPromptVoiceAttackFolderAsync(System.Windows.Controls.TextBox folderDisplayControl)
        {
            string? savedFolder = Settings.Default.StarCitizenLIA;

            if (string.IsNullOrEmpty(savedFolder) || !Directory.Exists(savedFolder))
            {
                var foundFolder = await FindVoiceAttackImportFolderAsync();

                if (!string.IsNullOrEmpty(foundFolder))
                {
                    Settings.Default.StarCitizenLIA = foundFolder;
                    Settings.Default.Save();

                    folderDisplayControl.Text = foundFolder;

                    return foundFolder;
                }
                else
                {
                    _toastService.ShowToast("Не вдалося знайти папку VoiceAttack. Будь ласка, оберіть вручну.");
                    return null;
                }
            }

            folderDisplayControl.Text = savedFolder;

            return savedFolder;
        }
    }
}
