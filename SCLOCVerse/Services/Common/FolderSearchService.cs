using SCLOCVerse.Interfaces;
using System.IO;

namespace SCLOCVerse.Services.Common
{
    public class FolderSearchService : IFolderSearchService
    {
        private readonly IIgnoreRulesProvider _ignoreRulesProvider;

        public FolderSearchService(IIgnoreRulesProvider ignoreRulesProvider)
        {
            _ignoreRulesProvider = ignoreRulesProvider;
        }

        public async Task<string?> FindOnFixedDrivesAsync(string targetFolderName, int maxDepth, CancellationToken cancellationToken)
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var found = await SearchDirectoryAsync(drive.RootDirectory.FullName, targetFolderName, maxDepth, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(found))
                {
                    return found;
                }
            }

            return null;
        }

        public async Task<string?> SearchDirectoryAsync(string rootDirectory, string targetFolderName, int maxDepth, CancellationToken cancellationToken)
        {
            return await Task.Run(() => DepthFirstSearch(rootDirectory, targetFolderName, maxDepth, 0, cancellationToken), cancellationToken).ConfigureAwait(false);
        }

        public IEnumerable<string> EnumerateAccessibleDirectories(string rootDirectory)
        {
            try
            {
                return Directory.EnumerateDirectories(rootDirectory)
                    .Where(dir => !_ignoreRulesProvider.ShouldIgnore(Path.GetFileName(dir)));
            }
            catch
            {
                return Enumerable.Empty<string>();
            }
        }

        // TODO(architecture): список середовищ дублюється у Controls/EnvironmentSelector.xaml.cs.
        // Свідомо не винесено в спільне місце, щоб не створювати новий файл і не тягнути
        // залежність шару Services від шару Controls. Винести при появі третього місця використання.
        private static readonly string[] GameEnvironments = { "LIVE", "PTU", "EPTU", "HOTFIX" };

        // Папка StarCitizen вважається справжнім коренем гри лише за наявності хоча б одного середовища.
        // Без цієї перевірки будь-яка папка з іменем StarCitizen (бэкап, dev-копія тощо) визнавалася б грою.
        private static bool IsValidGameRoot(string root)
        {
            foreach (var env in GameEnvironments)
            {
                if (Directory.Exists(Path.Combine(root, env)))
                    return true;
            }
            return false;
        }

        private string? DepthFirstSearch(string root, string targetFolder, int maxDepth, int currentDepth, CancellationToken token)
        {
            if (currentDepth > maxDepth || token.IsCancellationRequested)
                return null;

            try
            {
                if (Path.GetFileName(root).Equals(targetFolder, StringComparison.OrdinalIgnoreCase)
                    && IsValidGameRoot(root))
                {
                    return root;
                }

                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    if (_ignoreRulesProvider.ShouldIgnore(Path.GetFileName(dir)))
                        continue;

                    var found = DepthFirstSearch(dir, targetFolder, maxDepth, currentDepth + 1, token);
                    if (!string.IsNullOrEmpty(found))
                        return found;
                }
            }
            catch
            {
                // ► недоступні каталоги ігноруємо
            }

            return null;
        }
    }
}
