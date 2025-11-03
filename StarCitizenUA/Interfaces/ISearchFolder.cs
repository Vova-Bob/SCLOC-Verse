using System.Windows.Controls;

namespace StarCitizenUA
{
    public interface ISearchFolder
    {
        Task<string?> GetOrPromptGameFolderAsync(TextBox folderDisplayControl, CancellationToken cancellationToken);
        Task<string?> FindGameFolderAsync(int maxDepth, CancellationToken cancellationToken);
    }
}
