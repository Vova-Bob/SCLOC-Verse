using System;

namespace StarCitizenUA.Helpers
{
    public static class UpdateConstants
    {
        public const string UserAgent = "SCLocalizationUA";

        public const string UpdateDirectoryName = "SCLocalizationUA";
        public const string CacheFileName = "update-cache.json";

        public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

        public const string AppMutexName = "SCLocalizationUA_SingleInstanceMutex";
    }
}
