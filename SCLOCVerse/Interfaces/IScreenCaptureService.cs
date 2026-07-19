using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Сервіс захоплення регіону екрана для OCR Platform.
    /// Абстракція з двома планованими реалізаціями:
    /// <list type="bullet">
    /// <item><c>GdiScreenCaptureService</c> — Graphics.CopyFromScreen (default, borderless fullscreen).</item>
    /// <item><c>WindowsGraphicsCaptureService</c> — WinRT Windows.Graphics.Capture (exclusive fullscreen).</item>
    /// </list>
    /// </summary>
    public interface IScreenCaptureService
    {
        /// <summary>
        /// Чи підтримує ця реалізація exclusive fullscreen режим.
        /// GDI — ні (працює лише з borderless / windowed).
        /// Windows.Graphics.Capture — так.
        /// </summary>
        bool SupportsExclusiveFullscreen { get; }

        /// <summary>
        /// Захопити прямокутний регіон екрана та повернути його як BitmapSource (WPF-compatible).
        /// Координати <paramref name="region"/> — у фізичних пікселях екрана.
        /// </summary>
        /// <param name="region">Прямокутник захоплення у фізичних пікселях.</param>
        /// <param name="ct">Токен скасування.</param>
        /// <returns>BitmapSource захопленого регіону (BGRA, 96 DPI).</returns>
        Task<BitmapSource> CaptureRegionAsync(Rect region, CancellationToken ct = default);
    }
}
