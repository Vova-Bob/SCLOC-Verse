using System;

namespace SCLOCVerse.Helpers
{
    public static class UpdateConstants
    {
        public const string UserAgent = "SCLOC-Verse";

        public const string UpdateDirectoryName = "SCLOCVerse";
        public const string CacheFileName = "update-cache.json";
        public const string UpdateHistoryFileName = "update-history.json";
        public const string UpdatesDirectoryName = "Updates";
        public const string SetupAssetName = "SCLOC-Verse_Setup.exe";
        public const string ChecksumAssetName = "SCLOC-Verse_Setup.exe.sha256";

        public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

        public static readonly TimeSpan BackgroundUpdateCheckInterval = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan StartupUpdateCheckDelay = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan UpdatePanelAutoHideDelay = TimeSpan.FromSeconds(2.5);
    }
}
