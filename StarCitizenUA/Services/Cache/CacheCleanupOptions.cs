using System;
using System.IO;

namespace StarCitizenUA.Services.Cache
{
    public class CacheCleanupOptions
    {
        private const double LatestOkGigabytes = 1.5d;
        private const double BigDirGigabytes = 3.0d;

        public CacheCleanupOptions()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            CacheRootPath = Path.Combine(localAppData, "Star Citizen");
            LatestOkBytes = (long)(LatestOkGigabytes * 1024 * 1024 * 1024);
            BigDirectoryBytes = (long)(BigDirGigabytes * 1024 * 1024 * 1024);
        }

        public string CacheRootPath { get; init; }

        public long LatestOkBytes { get; init; }

        public long BigDirectoryBytes { get; init; }

        public int DeleteRetryCount { get; init; } = 3;

        public TimeSpan DeleteRetryDelay { get; init; } = TimeSpan.FromMilliseconds(300);

        public string CacheRelativePath { get; init; } = Path.Combine("cache", "shaders");
    }
}
