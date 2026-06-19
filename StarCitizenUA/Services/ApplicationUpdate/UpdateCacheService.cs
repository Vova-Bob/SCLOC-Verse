using Newtonsoft.Json;
using StarCitizenUA.Interfaces;
using StarCitizenUA.Models.ApplicationUpdate;
using System;
using System.Collections.Generic;
using System.IO;

namespace StarCitizenUA.Services.ApplicationUpdate
{
    public class UpdateCacheService : IUpdateCacheService
    {
        private readonly string _cacheFilePath;

        public UpdateCacheService()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var cacheDirectory = Path.Combine(localAppData, "SCLocalizationUA");

            if (!Directory.Exists(cacheDirectory))
                Directory.CreateDirectory(cacheDirectory);

            _cacheFilePath = Path.Combine(cacheDirectory, "update-cache.json");
        }

        public UpdateCacheEntry? Read(UpdateChannel channel)
        {
            if (!File.Exists(_cacheFilePath))
                return null;

            var json = File.ReadAllText(_cacheFilePath);

            var entry = JsonConvert.DeserializeObject<UpdateCacheEntry>(json);
            if (entry == null || entry.Channel != channel)
                return null;

            return entry;
        }

        public void Write(UpdateChannel channel, List<GitHubRelease> releases, TimeSpan ttl)
        {
            var entry = new UpdateCacheEntry
            {
                CachedAt = DateTimeOffset.UtcNow,
                Channel = channel,
                Releases = releases
            };

            var json = JsonConvert.SerializeObject(entry, Formatting.Indented);
            File.WriteAllText(_cacheFilePath, json);
        }

        public bool IsValid(UpdateCacheEntry entry, TimeSpan ttl)
        {
            if (entry == null)
                return false;

            var expiry = entry.CachedAt.Add(ttl);
            return DateTimeOffset.UtcNow <= expiry;
        }

        public void Clear()
        {
            if (File.Exists(_cacheFilePath))
                File.Delete(_cacheFilePath);
        }
    }
}
