using StarCitizenUA.Interfaces;
using System.IO;
using System.Windows.Controls;

namespace StarCitizenUA
{
    class SearchFolder : ISearchFolder
    {
        private IToastService _toastService;
        public SearchFolder(IToastService toastService)
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
        
        public Task<string?> GetOrPromptGameFolderAsync(TextBox folderDisplayControl)
        {
            string? savedFolder = Settings.Default.StarCitizenUA;

            if (!string.IsNullOrEmpty(savedFolder) && Directory.Exists(savedFolder))
            {
                folderDisplayControl.Text = savedFolder;
                return Task.FromResult<string?>(savedFolder);
            }
            else
            {
                _toastService.ShowToast("Натисніть кнопку автопошук або оберіть шлях вручну.");
            }

            return Task.FromResult<string?>(null);
        }

        public async Task<string?> FindGameFolder(int maxDepth)
        {
            return await Task.Run(() =>
            {
                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                {
                    try
                    {
                        var folder = SearchDirectory(drive.RootDirectory.FullName, "StarCitizen", maxDepth);
                        if (!string.IsNullOrEmpty(folder))
                            return folder;
                    }
                    catch { continue; }
                }
                return null;
            });
        }

        public string? SearchDirectory(string root, string targetFolder, int maxDepth, int currentDepth = 0)
        {
            if (currentDepth > maxDepth) return null;

            try
            {
                if (Path.GetFileName(root).Equals(targetFolder, StringComparison.OrdinalIgnoreCase))
                    return root;

                foreach (var dir in Directory.EnumerateDirectories(root)
                                             .Where(d => !IgnoredDirs.Contains(Path.GetFileName(d))))
                {
                    var found = SearchDirectory(dir, targetFolder, maxDepth, currentDepth + 1);
                    if (!string.IsNullOrEmpty(found))
                        return found;
                }
            }
            catch
            {
                // Ігноруємо недоступні каталоги
            }

            return null;
        }      
    }
}
