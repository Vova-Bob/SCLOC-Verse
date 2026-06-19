using StarCitizenUA.Models.ApplicationUpdate;
using System;
using System.Collections.Generic;

namespace StarCitizenUA.Interfaces
{
    public interface IUpdateCacheService
    {
        UpdateCacheEntry? Read(UpdateChannel channel);
        void Write(UpdateChannel channel, List<GitHubRelease> releases, TimeSpan ttl);
        bool IsValid(UpdateCacheEntry entry, TimeSpan ttl);
        void Clear();
    }
}
