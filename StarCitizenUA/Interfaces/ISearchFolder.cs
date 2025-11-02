namespace StarCitizenUA
{
    public interface ISearchFolder
    {
        Task<string?> GetOrPromptGameFolderAsync(System.Windows.Controls.TextBox folderDisplayControl);
        Task<string?> FindGameFolder(int maxDepth);
        string? SearchDirectory(string root, string targetFolder, int maxDepth, int currentDepth = 0);
    }
}
