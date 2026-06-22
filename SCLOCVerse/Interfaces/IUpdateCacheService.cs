using SCLOCVerse.Models.ApplicationUpdate;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SCLOCVerse.Interfaces
{
    public interface IUpdateCacheService
    {
        Task<UpdateCacheEntry?> ReadAsync(UpdateChannel channel);
        Task WriteAsync(UpdateChannel channel, List<GitHubRelease> releases);
        bool IsValid(UpdateCacheEntry? entry, TimeSpan ttl);
        Task ClearAsync();
    }
}
