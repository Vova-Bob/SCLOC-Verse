using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace StarCitizenUA.Interfaces
{
    public interface IVoiceAttackFolderHelper
    {
        Task<string?> FindVoiceAttackImportFolderAsync(CancellationToken cancellationToken);
        Task<string?> GetOrPromptVoiceAttackFolderAsync(TextBox folderDisplayControl, CancellationToken cancellationToken);
        void CancelActiveSearch();
    }
}
