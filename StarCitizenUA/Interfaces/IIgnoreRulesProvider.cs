using System.Collections.Generic;

namespace StarCitizenUA.Interfaces
{
    public interface IIgnoreRulesProvider
    {
        bool ShouldIgnore(string directoryName);
        IReadOnlyCollection<string> GetIgnoredDirectories();
    }
}
