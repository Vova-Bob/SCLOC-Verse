using System;

namespace StarCitizenUA.Services.Cache
{
    public class ShaderCacheEntry
    {
        public ShaderCacheEntry(string displayName, string fullPath, long sizeBytes, DateTime lastWriteTimeUtc)
        {
            DisplayName = displayName;
            FullPath = fullPath;
            SizeBytes = sizeBytes;
            LastWriteTimeUtc = lastWriteTimeUtc;
        }

        public string DisplayName { get; }

        public string FullPath { get; }

        public long SizeBytes { get; }

        public DateTime LastWriteTimeUtc { get; }
    }
}
