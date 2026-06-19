using System;

namespace StarCitizenUA.Models.ApplicationUpdate
{
    public class UpdateHistoryEntry
    {
        public DateTimeOffset Timestamp { get; set; }
        public Version FromVersion { get; set; } = new Version(0, 0, 0, 0);
        public Version ToVersion { get; set; } = new Version(0, 0, 0, 0);
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
