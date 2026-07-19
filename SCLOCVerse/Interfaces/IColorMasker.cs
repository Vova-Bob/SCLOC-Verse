using OpenCvSharp;
using SCLOCVerse.Models.OcrPlatform;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Ізоляція кольорового тексту через HSV маскування.
    /// Використовується перед OCR для:
    /// <list type="bullet">
    /// <item>Виявлення наявності HUD-тексту певного кольору (напр. жовтий mining HUD).</item>
    /// <item>Ізоляція цифр від фону (поліпшення точності Adaptive Threshold).</item>
    /// </list>
    /// </summary>
    public interface IColorMasker
    {
        /// <summary>
        /// Створити бінарну маску з вхідного BGR зображення за діапазоном HSV.
        /// Пікселі всередині діапазону = 255 (білі), поза = 0 (чорні).
        /// </summary>
        /// <param name="inputBgr">Вхідне зображення у форматі BGR.</param>
        /// <param name="range">HSV діапазон для ізоляції.</param>
        /// <returns>Новий Mat (8UC1, бінарна маска) — викликач відповідає за Dispose.</returns>
        Mat Mask(Mat inputBgr, HsvRange range);
    }
}
