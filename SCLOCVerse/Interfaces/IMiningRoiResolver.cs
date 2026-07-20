using SCLOCVerse.Models.Mining;
using System.Windows;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Динамічний ROI Resolver для Mining Module.
    ///
    /// Архітектурний принцип:
    /// "Screen resolution is never the source of truth. The HUD layout is."
    ///
    /// Двоступенева архітектура:
    /// 1. Discovery Mode — IMiningHudLocatorStrategy локалізує HUD (БЕЗ OCR).
    /// 2. Tracking Mode — OCR всередині знайдених регіонів + confidence-based transitions.
    ///
    /// Transitions:
    /// Discovery + HUD found → Tracking (записати bounds + обчислити regions).
    /// Tracking + high confidence → оновити regions (HUD може плавати).
    /// Tracking + low confidence (soft) → grace cycles → Discovery.
    /// Tracking + very low confidence (hard) → негайно Discovery.
    /// Tracking + null × N → Discovery (повна втрата сигналу).
    /// </summary>
    public interface IMiningRoiResolver
    {
        /// <summary>Поточний стан layout (mode + HUD bounds + regions + confidence).</summary>
        MiningHudLayout Layout { get; }

        /// <summary>
        /// Поточний прямокутник екрана для захоплення.
        /// Discovery — повний екран (для locator'а).
        /// Tracking — HUD bounds (для OCR всередині HUD).
        /// </summary>
        Rect CurrentCaptureRect { get; }

        /// <summary>
        /// Повідомити resolver, що locator знайшов HUD (Discovery → Tracking).
        /// Обчислює абсолютні bounds регіонів з RelativeBounds × HUD bounds.
        /// </summary>
        void OnHudLocated(MiningHudLocationResult location);

        /// <summary>
        /// Повідомити resolver про результат OCR одного регіону (Tracking).
        /// Resolver оновлює per-region state та перевіряє confidence thresholds.
        /// </summary>
        /// <param name="regionName">Ім'я регіону (напр. "signature", "distance").</param>
        /// <param name="ocrText">Розпізнаний текст (null = нічого не знайдено).</param>
        /// <param name="ocrConfidence">Confidence OCR [0..1] (0 = null/failed).</param>
        void OnRegionResult(string regionName, string? ocrText, double ocrConfidence);

        /// <summary>Скинути resolver до Discovery mode (втрата HUD / Disable).</summary>
        void ResetToDiscovery();
    }
}