using OpenCvSharp;
using SCLOCVerse.Models.Mining;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Стратегія локалізації Mining HUD на скріншоті.
    ///
    /// Архітектурний принцип:
    /// "Screen resolution is never the source of truth. The HUD layout is."
    ///
    /// Discovery Mode НЕ запускає OCR — він лише локалізує HUD (дешева операція).
    /// Лише після локалізації HUD запускається OCR всередині знайдених регіонів.
    ///
    /// Реалізації (стратегії) — алгоритм-незалежний контракт:
    /// вхідний Mat → результат або null. Архітектура не знає алгоритм.
    ///
    /// Можливі стратегії (не реалізовано — потребують архітектурного рішення):
    /// <list type="bullet">
    /// <item><c>TemplateLocatorStrategy</c> — template matching (NCC).</item>
    /// <item><c>FeatureLocatorStrategy</c> — ORB/SIFT/AKAZE feature matching.</item>
    /// <item><c>DnnLocatorStrategy</c> — OpenCV DNN / YOLO object detection.</item>
    /// <item><c>HybridLocatorStrategy</c> — комбінація кількох стратегій.</item>
    /// </list>
    ///
    /// ВАЖЛИВО: стратегії НЕ повинні залежати від кольору (HUD color, gamma, HDR, Reshade
    /// можуть змінюватись користувачем). Текстура/структура/форма — стабільніші ознаки.
    /// </summary>
    public interface IMiningHudLocatorStrategy
    {
        /// <summary>Ім'я стратегії (для debug логів).</summary>
        string Name { get; }

        /// <summary>
        /// Локалізувати Mining HUD на скріншоті.
        ///
        /// НЕ запускає OCR — лише дешевий пошук (color/template/feature).
        /// Якщо HUD не знайдено → повертає null (Discovery лишається активним).
        /// </summary>
        /// <param name="screenshot">Повний скріншот екрана (BGR Mat, будь-яка роздільна здатність).</param>
        /// <returns>Bounds знайденого HUD з confidence, АБО null якщо HUD не виявлено.</returns>
        MiningHudLocationResult? Locate(Mat screenshot);
    }
}