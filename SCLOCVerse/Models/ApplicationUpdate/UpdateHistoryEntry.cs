using System;

namespace SCLOCVerse.Models.ApplicationUpdate
{
    public class UpdateHistoryEntry
    {
        public DateTimeOffset Timestamp { get; set; }
        public UpdateChannel Channel { get; set; }
        public Version FromVersion { get; set; } = new Version(0, 0, 0, 0);
        public Version ToVersion { get; set; } = new Version(0, 0, 0, 0);
        public UpdateOperation Operation { get; set; }
        public UpdateOperationResult Result { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
