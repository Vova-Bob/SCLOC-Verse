using System;
using System.IO;
using System.Linq;

namespace SCLOCVerse.Helpers
{
    /// <summary>
    /// Єдине джерело списку середовищ Star Citizen та мапінгу каналів оновлення.
    /// Прибрано дублювання: FolderSearchService + EnvironmentSelector + BackgroundUpdateOrchestrator.
    ///
    /// Бізнес-логіка (відображає модель випуску локалізації Cloud Imperium):
    ///   LIVE, HOTFIX  → GitHub Release
    ///   PTU, EPTU     → GitHub Pre-release
    /// </summary>
    public static class StarCitizenEnvironments
    {
        /// <summary>
        /// Усі відомі середовища Star Citizen у канонічному порядку.
        /// </summary>
        public static readonly string[] Known = { "LIVE", "PTU", "EPTU", "HOTFIX" };

        /// <summary>
        /// Визначає, чи середовище використовує pre-release канал GitHub.
        /// PTU та EPTU — pre-release; LIVE та HOTFIX — release.
        /// Явне Equals замість Contains — бізнес-логіка не залежить від підрядка.
        /// </summary>
        public static bool IsPrereleaseChannel(string environmentName)
            => environmentName.Equals("PTU", StringComparison.OrdinalIgnoreCase)
               || environmentName.Equals("EPTU", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Виявляє, які з відомих середовищ існують у корені гри (LIVE/PTU/EPTU/HOTFIX).
        /// Additive: надає Settings Hub реальний стан (бейджі середовищ, лічильник «Локалізація»).
        /// </summary>
        public static string[] DetectExisting(string? gameRoot)
        {
            if (string.IsNullOrWhiteSpace(gameRoot))
                return Array.Empty<string>();

            return Known
                .Where(env => Directory.Exists(Path.Combine(gameRoot, env)))
                .ToArray();
        }
    }
}