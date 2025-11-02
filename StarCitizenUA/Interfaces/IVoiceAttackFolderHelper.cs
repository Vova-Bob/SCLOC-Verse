namespace StarCitizenUA.Interfaces
{
    public interface IVoiceAttackFolderHelper
    {
        Task<string?> FindVoiceAttackImportFolderAsync();
        Task<string?> GetOrPromptVoiceAttackFolderAsync(System.Windows.Controls.TextBox folderDisplayControl);
    }
}
