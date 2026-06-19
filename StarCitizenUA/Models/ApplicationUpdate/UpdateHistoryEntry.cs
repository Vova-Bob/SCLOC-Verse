using System;

namespace StarCitizenUA.Models.ApplicationUpdate
{
    public class UpdateHistoryEntry
    {
        public Version Version { get; set; } = new Version(0, 0, 0, 0);
        public DateTimeOffset Date { get; set; }
        public UpdateChannel Channel { get; set; }
        public UpdateOperationResult Result { get; set; }
    }
}
