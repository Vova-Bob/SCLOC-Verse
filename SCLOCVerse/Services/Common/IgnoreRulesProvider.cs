using SCLOCVerse.Interfaces;

namespace SCLOCVerse.Services.Common
{
    public class IgnoreRulesProvider : IIgnoreRulesProvider
    {
        private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
        {
            "$Recycle.Bin",
            "System Volume Information",
            "Config.Msi",
            "Windows",
            "ProgramData",
            "Recovery",
            "MSOCache"
        };

        public IReadOnlyCollection<string> GetIgnoredDirectories() => Ignored;

        public bool ShouldIgnore(string directoryName) => Ignored.Contains(directoryName);
    }
}
