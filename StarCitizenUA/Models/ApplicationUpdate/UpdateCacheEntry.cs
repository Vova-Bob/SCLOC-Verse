using System;

namespace StarCitizenUA.Models.ApplicationUpdate
{
    public class UpdateCacheEntry
    {
        public Version Version { get; set; } = new Version(0, 0, 0, 0);
        public string DownloadUrl { get; set; } = string.Empty;
        public string? Checksum { get; set; }
        public DateTimeOffset Expiry { get; set; }
    }
}
