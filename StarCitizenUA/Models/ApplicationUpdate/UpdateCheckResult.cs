using System;

namespace StarCitizenUA.Models.ApplicationUpdate
{
    public class UpdateCheckResult
    {
        public UpdateCheckStatus Status { get; set; }
        public Version? AvailableVersion { get; set; }
        public ReleaseAssetInfo? AssetInfo { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
