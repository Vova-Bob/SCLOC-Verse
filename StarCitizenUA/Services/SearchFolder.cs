using StarCitizenUA.Interfaces;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace StarCitizenUA
{
    class SearchFolder : ISearchFolder
    {
        private readonly IFolderSearchService _folderSearchService;
        private readonly ISettingsService _settingsService;

        public SearchFolder(IFolderSearchService folderSearchService, ISettingsService settingsService)
        {
            _folderSearchService = folderSearchService;
            _settingsService = settingsService;
        }

        public Task<string?> GetOrPromptGameFolderAsync(TextBox folderDisplayControl, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var savedFolder = _settingsService.GetGameFolder();
            if (!string.IsNullOrEmpty(savedFolder))
            {
                folderDisplayControl.Text = savedFolder;
                return Task.FromResult<string?>(savedFolder);
            }

            return Task.FromResult<string?>(null);
        }

        public async Task<string?> FindGameFolderAsync(int maxDepth, CancellationToken cancellationToken)
        {
            var found = await _folderSearchService.FindOnFixedDrivesAsync("StarCitizen", maxDepth, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(found))
            {
                if (_settingsService.TrySetGameFolder(found))
                {
                    return found;
                }
            }

            return null;
        }
    }
}
