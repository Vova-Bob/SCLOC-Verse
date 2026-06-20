using System;
using System.Collections.Generic;

namespace StarCitizenUA.Models.ApplicationUpdate
{
    public class UpdateCacheEntry
    {
        public DateTimeOffset CachedAt { get; set; }
        public UpdateChannel Channel { get; set; }
        public List<GitHubRelease> Releases { get; set; } = new();
    }
}
