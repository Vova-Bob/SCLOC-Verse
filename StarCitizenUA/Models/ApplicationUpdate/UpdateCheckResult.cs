using System;

namespace StarCitizenUA.Models.ApplicationUpdate
{
    public class UpdateCheckResult
    {
        public bool IsUpdateAvailable { get; set; }
        public Version CurrentVersion { get; set; } = new Version(0, 0, 0, 0);
        public Version LatestVersion { get; set; } = new Version(0, 0, 0, 0);
        public string DownloadUrl { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
        public UpdateChannel Channel { get; set; }
        public UpdateCheckStatus Status { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
