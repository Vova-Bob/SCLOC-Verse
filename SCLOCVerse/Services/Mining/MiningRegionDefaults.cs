using System.Windows;

namespace SCLOCVerse.Services.Mining
{
    /// <summary>
    /// Hardcoded координати регіонів SC mining HUD для типових resolutions.
    /// Placeholder — реальні координати уточняться після тестування на SC HUD.
    ///
    /// Формула масштабування: linear interpolation від 1080p reference.
    /// 1440p: multiplier = 1.5. 4K: multiplier = 2.0.
    ///
    /// Після впровадження Calibration Tool (T9.1) користувач зможе override ці значення.
    /// </summary>
    public static class MiningRegionDefaults
    {
        /// <summary>Стандартні регіони для 1080p (reference resolution).</summary>
        public static readonly (Rect MaterialCode, Rect ClusterCount) Defaults1080p = (
            // SC mining HUD: material code знаходиться зазвичай у top-right zone scanner panel.
            MaterialCode: new Rect(1620, 380, 80, 24),
            // Cluster count — нижче на ~30px.
            ClusterCount: new Rect(1620, 410, 60, 24)
        );

        /// <summary>Стандартні регіони для 1440p (×1.5 масштаб).</summary>
        public static readonly (Rect MaterialCode, Rect ClusterCount) Defaults1440p = (
            MaterialCode: new Rect(2430, 570, 120, 36),
            ClusterCount: new Rect(2430, 615, 90, 36)
        );

        /// <summary>Стандартні регіони для 4K (×2.0 масштаб).</summary>
        public static readonly (Rect MaterialCode, Rect ClusterCount) Defaults4K = (
            MaterialCode: new Rect(3240, 760, 160, 48),
            ClusterCount: new Rect(3240, 820, 120, 48)
        );

        /// <summary>
        /// Визначити регіони за поточним primary screen resolution.
        /// Повертає closest known resolution (1080p / 1440p / 4K) або linear scale.
        /// </summary>
        public static (Rect MaterialCode, Rect ClusterCount) GetForCurrentResolution()
        {
            var screenWidth = (int)SystemParameters.PrimaryScreenWidth;
            var screenHeight = (int)SystemParameters.PrimaryScreenHeight;

            // Класифікація за height (найстабільніший индикатор).
            return screenHeight switch
            {
                >= 2000 => Defaults4K,            // 4K (2160p)
                >= 1400 => Defaults1440p,         // 1440p
                _ => Defaults1080p               // 1080p або менше (default)
            };
        }
    }
}