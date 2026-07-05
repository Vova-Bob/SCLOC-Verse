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

        // Single Instance + IPC (Mutex + Named Pipe).
        // Ім'я Mutex без префікса Local\ лишається сесійним за замовчуванням і
        // зберігає сумісність з уже запущеними екземплярами попередніх версій.
        public const string SingleInstanceMutexName = "SCLOCVerse_SingleInstanceMutex";
        public const string SingleInstancePipeName = "SCLOCVerse_IPC";

        /// <summary>
        /// Таймаут підключення другого процесу до pipe першого.
        /// Якщо перший процес не відповідає — вважаємо його неактивним.
        /// </summary>
        public static readonly TimeSpan SingleInstanceConnectTimeout = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Таймаут очікування підтвердження від першого процесу після відправки команди.
        /// </summary>
        public static readonly TimeSpan SingleInstanceResponseTimeout = TimeSpan.FromSeconds(2);
    }
}
