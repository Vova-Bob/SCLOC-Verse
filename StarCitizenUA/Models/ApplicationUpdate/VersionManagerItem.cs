using System;
using StarCitizenUA.Models.ApplicationUpdate;

namespace StarCitizenUA.Models.ApplicationUpdate
{
    public class VersionManagerItem
    {
        public DateTimeOffset ReleaseDate { get; set; }
        public string Version { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string FullDescription { get; set; } = string.Empty;
        public string ReleaseUrl { get; set; } = string.Empty;
        public bool IsInstalled { get; set; }
        public bool CanInstall => !IsInstalled;
        public bool HasDetails { get; set; }
        public string StatusText { get; set; } = string.Empty;
        public string StatusColor { get; set; } = "#808080";
        public UpdateChannel Channel { get; set; }
        public GitHubRelease Release { get; set; } = new();
    }
}
