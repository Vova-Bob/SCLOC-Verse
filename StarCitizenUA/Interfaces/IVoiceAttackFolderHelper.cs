namespace StarCitizenUA.Interfaces
{
    public interface IVoiceAttackFolderHelper
    {
        Task<string?> FindVoiceAttackImportFolderAsync();
        Task<string?> SearchDirectoryAsync(string currentDir, string targetFolder, int maxDepth, int currentDepth = 0);
        Task<string?> GetOrPromptVoiceAttackFolderAsync(System.Windows.Controls.TextBox folderDisplayControl);
    }
}
