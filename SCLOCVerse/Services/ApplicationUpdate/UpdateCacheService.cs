using Newtonsoft.Json;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.ApplicationUpdate;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SCLOCVerse.Services.ApplicationUpdate
{
    public class UpdateCacheService : IUpdateCacheService
    {
        private readonly string _cacheFilePath;

        public UpdateCacheService()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var cacheDirectory = Path.Combine(localAppData, "SCLOCVerse");

            if (!Directory.Exists(cacheDirectory))
                Directory.CreateDirectory(cacheDirectory);

            _cacheFilePath = Path.Combine(cacheDirectory, "update-cache.json");
        }

        public async Task<UpdateCacheEntry?> ReadAsync(UpdateChannel channel)
        {
            if (!File.Exists(_cacheFilePath))
                return null;

            var json = await File.ReadAllTextAsync(_cacheFilePath).ConfigureAwait(false);

            var entry = JsonConvert.DeserializeObject<UpdateCacheEntry>(json);
            if (entry == null || entry.Channel != channel)
                return null;

            return entry;
        }

        public async Task WriteAsync(UpdateChannel channel, List<GitHubRelease> releases)
        {
            var entry = new UpdateCacheEntry
            {
                CachedAt = DateTimeOffset.UtcNow,
                Channel = channel,
                Releases = releases
            };

            var json = JsonConvert.SerializeObject(entry, Formatting.Indented);
            await File.WriteAllTextAsync(_cacheFilePath, json).ConfigureAwait(false);
        }

        public bool IsValid(UpdateCacheEntry? entry, TimeSpan ttl)
        {
            if (entry == null)
                return false;

            return DateTimeOffset.UtcNow <= entry.CachedAt.Add(ttl);
        }

        public async Task ClearAsync()
        {
            if (File.Exists(_cacheFilePath))
            {
                await Task.Run(() => File.Delete(_cacheFilePath)).ConfigureAwait(false);
            }
        }
    }
}
