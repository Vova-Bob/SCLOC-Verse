namespace SCLOCVerse.Interfaces
{
    public interface ISettingsService
    {
        string? GetGameFolder();
        bool TrySetGameFolder(string? path);
        void ClearGameFolder();
    }
}
