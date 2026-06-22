namespace SCLOCVerse.Services.Cache
{
    public class ShaderCacheInspection
    {
        public ShaderCacheInspection(CacheCleanupOptions options, IReadOnlyList<ShaderCacheEntry> entries)
        {
            Options = options;
            Entries = entries.OrderByDescending(e => e.LastWriteTimeUtc).ToList();
            Latest = Entries.FirstOrDefault();
            HasOlder = Entries.Count > 1;
            HasBigDirectories = Entries.Any(e => e.SizeBytes >= Options.BigDirectoryBytes);
            LatestTooLarge = Latest != null && Latest.SizeBytes > Options.LatestOkBytes;
            TotalBytes = Entries.Sum(e => e.SizeBytes);
        }

        public CacheCleanupOptions Options { get; }

        public IReadOnlyList<ShaderCacheEntry> Entries { get; }

        public ShaderCacheEntry? Latest { get; }

        public bool HasCache => Entries.Count > 0;

        public bool HasOlder { get; }

        public bool HasBigDirectories { get; }

        public bool LatestTooLarge { get; }

        public long TotalBytes { get; }
    }
}
