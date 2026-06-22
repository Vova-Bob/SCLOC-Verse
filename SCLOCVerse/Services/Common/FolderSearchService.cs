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

        private string? DepthFirstSearch(string root, string targetFolder, int maxDepth, int currentDepth, CancellationToken token)
        {
            if (currentDepth > maxDepth || token.IsCancellationRequested)
                return null;

            try
            {
                if (Path.GetFileName(root).Equals(targetFolder, StringComparison.OrdinalIgnoreCase))
                    return root;

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
