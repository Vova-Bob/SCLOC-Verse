namespace SCLOCVerse.Interfaces
{
    public interface IIgnoreRulesProvider
    {
        bool ShouldIgnore(string directoryName);
        IReadOnlyCollection<string> GetIgnoredDirectories();
    }
}
