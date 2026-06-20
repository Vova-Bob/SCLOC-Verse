using System;

namespace StarCitizenUA.Helpers
{
    public static class UpdateConstants
    {
        public const string UserAgent = "SCLocalizationUA";

        public const string UpdateDirectoryName = "SCLocalizationUA";
        public const string CacheFileName = "update-cache.json";
        public const string UpdateHistoryFileName = "update-history.json";
        public const string UpdatesDirectoryName = "Updates";

        public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

        public static readonly TimeSpan BackgroundUpdateCheckInterval = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan StartupUpdateCheckDelay = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan UpdatePanelAutoHideDelay = TimeSpan.FromSeconds(2.5);
    }
}
