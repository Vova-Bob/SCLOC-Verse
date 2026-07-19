using SCLOCVerse.Interfaces;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace SCLOCVerse.Services.OcrPlatform.Captures
{
    /// <summary>
    /// Реалізація <see cref="IScreenCaptureService"/> через System.Drawing.Graphics.CopyFromScreen.
    /// Підтримує borderless fullscreen та windowed режими Star Citizen.
    /// НЕ підтримує exclusive fullscreen (для цього потрібен Windows.Graphics.Capture).
    ///
    /// Захоплення відбувається у фізичних пікселях екрана. Процес має бути
    /// DPI-aware (per-monitor v2) — інакше координати будуть масштабовані.
    /// SCLOCVerse оголошує DPI-awareness через app.manifest (якщо потрібно).
    /// </summary>
    public sealed class GdiScreenCaptureService : IScreenCaptureService
    {
        /// <inheritdoc />
        public bool SupportsExclusiveFullscreen => false;

        /// <inheritdoc />
        public Task<BitmapSource> CaptureRegionAsync(Rect region, CancellationToken ct = default)
        {
            // CopyFromScreen — синхронний CPU-bound виклик. Запускаємо на ThreadPool,
            // щоб не блокувати UI-диспетчер (Coordinators викликає з TimerCallback).
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                var width = (int)System.Math.Ceiling(region.Width);
                var height = (int)System.Math.Ceiling(region.Height);
                if (width <= 0 || height <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(region),
                        $"Регіон захоплення має ненульовий розмір: {width}x{height}");
                }

                // Format32bppArgb — найшвидший формат для CopyFromScreen + збереження
                // альфа-каналу. Конвертація в BGRA BitmapSource тривіальна.
                using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using var graphics = Graphics.FromImage(bitmap);

                graphics.CopyFromScreen(
                    sourceX: (int)System.Math.Floor(region.X),
                    sourceY: (int)System.Math.Floor(region.Y),
                    destinationX: 0,
                    destinationY: 0,
                    blockRegionSize: bitmap.Size);

                ct.ThrowIfCancellationRequested();

                return ConvertToBitmapSource(bitmap);
            }, ct);
        }

        /// <summary>
        /// Конвертує System.Drawing.Bitmap → WPF BitmapSource без копіювання пікселів
        /// (через HBitmap). Frozen для cross-thread використання.
        /// </summary>
        private static BitmapSource ConvertToBitmapSource(Bitmap bitmap)
        {
            var hBitmap = bitmap.GetHbitmap();

            try
            {
                var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                // Frozen — можна використовувати з будь-якого потоку (Coordinator → MiningService).
                bitmapSource.Freeze();
                return bitmapSource;
            }
            finally
            {
                // Звільнення HBitmap обов'язкове — це unmanaged ресурс GDI.
                DeleteObject(hBitmap);
            }
        }

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
