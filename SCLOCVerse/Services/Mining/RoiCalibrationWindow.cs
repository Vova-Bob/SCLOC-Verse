using OpenCvSharp;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.OcrPlatform;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Rect = System.Windows.Rect;
using Point = System.Windows.Point;
using Window = System.Windows.Window;

namespace SCLOCVerse.Services.Mining
{
    /// <summary>
    /// Вікно ручного калібрування ROI для Signature Scanner.
    ///
    /// Затемнює екран, дозволяє виділити/перетягувати/ресайзити область.
    /// Показує Live Preview OCR: розпізнаний текст + confidence в реальному часі.
    ///
    /// Зберігає Rect (X, Y, Width, Height) — без прив'язки до алгоритмів.
    /// </summary>
    public sealed class RoiCalibrationWindow : Window
    {
        private readonly IOcrEngine _ocrEngine;
        private readonly IScreenCaptureService _screenCapture;
        private System.Threading.Timer? _previewTimer;
        private Rect _roi;
        private bool _hasSelection;
        private bool _isDragging;
        private bool _isResizing;
        private Point _dragStart;
        private const int HandleSize = 20;

        private readonly TextBlock _instructionLabel;
        private readonly TextBlock _previewLabel;
        private readonly TextBlock _textValue;
        private readonly TextBlock _confidenceValue;
        private readonly Rectangle _selectionRect;
        private readonly Rectangle _resizeHandle;
        private readonly Canvas _canvas;

        public Rect? Result { get; private set; }

        public RoiCalibrationWindow(IOcrEngine ocrEngine, IScreenCaptureService screenCapture)
        {
            _ocrEngine = ocrEngine;
            _screenCapture = screenCapture;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = true;
            Cursor = Cursors.Cross;

            var screen = SystemParameters.WorkArea;
            Width = screen.Width;
            Height = screen.Height;
            Left = 0;
            Top = 0;

            _instructionLabel = new TextBlock
            {
                Text = "Виділіть область сигнатури\n\nЛКМ + тягнути = виділити\nПеретягувати центр = перемістити\nТягнути за кут = змінити розмір\nEnter = зберегти | Esc = скасувати",
                FontSize = 16,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 20, 0, 0)
            };

            _previewLabel = new TextBlock
            {
                Text = "Live Preview OCR",
                FontSize = 12,
                Foreground = Brushes.Cyan,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 60)
            };

            _textValue = new TextBlock
            {
                Text = "—",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 30)
            };

            _confidenceValue = new TextBlock
            {
                Text = "confidence: —",
                FontSize = 11,
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 10)
            };

            _selectionRect = new Rectangle
            {
                Stroke = Brushes.Cyan,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(30, 0, 255, 255)),
                Visibility = Visibility.Collapsed
            };

            _resizeHandle = new Rectangle
            {
                Width = HandleSize,
                Height = HandleSize,
                Fill = Brushes.Cyan,
                Stroke = Brushes.White,
                StrokeThickness = 1,
                Visibility = Visibility.Collapsed,
                Cursor = Cursors.SizeNWSE
            };

            _canvas = new Canvas();
            _canvas.Children.Add(_instructionLabel);
            Canvas.SetLeft(_instructionLabel, screen.Width / 2 - 200);
            _canvas.Children.Add(_previewLabel);
            Canvas.SetLeft(_previewLabel, screen.Width / 2 - 80);
            _canvas.Children.Add(_textValue);
            Canvas.SetLeft(_textValue, screen.Width / 2 - 80);
            _canvas.Children.Add(_confidenceValue);
            Canvas.SetLeft(_confidenceValue, screen.Width / 2 - 80);
            _canvas.Children.Add(_selectionRect);
            _canvas.Children.Add(_resizeHandle);

            Content = _canvas;

            KeyDown += OnKeyDown;
            MouseLeftButtonDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseLeftButtonUp += OnMouseUp;
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                StopPreview();
                Result = null;
                Close();
            }
            else if (e.Key == Key.Enter && _hasSelection)
            {
                StopPreview();
                Result = _roi;
                Close();
            }
        }

        private void OnMouseDown(object? sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(this);

            // Перевірити клік на resize handle.
            if (_hasSelection && HitResizeHandle(pos))
            {
                _isResizing = true;
                _dragStart = pos;
                return;
            }

            // Перевірити клік всередині selection (перетягування).
            if (_hasSelection && _roi.Contains(pos))
            {
                _isDragging = true;
                _dragStart = pos;
                return;
            }

            // Нове виділення.
            _isDragging = false;
            _isResizing = false;
            _roi = new Rect(pos.X, pos.Y, 0, 0);
            _dragStart = pos;
            _selectionRect.Visibility = Visibility.Visible;
        }

        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(this);

            if (_isResizing)
            {
                var newRight = Math.Max(_roi.X + 10, pos.X);
                var newBottom = Math.Max(_roi.Y + 10, pos.Y);
                _roi = new Rect(_roi.X, _roi.Y, newRight - _roi.X, newBottom - _roi.Y);
                UpdateSelectionVisuals();
            }
            else if (_isDragging)
            {
                var dx = pos.X - _dragStart.X;
                var dy = pos.Y - _dragStart.Y;
                _roi = new Rect(_roi.X + dx, _roi.Y + dy, _roi.Width, _roi.Height);
                _dragStart = pos;
                UpdateSelectionVisuals();
            }
            else if (_dragStart != default && !_hasSelection)
            {
                var x = Math.Min(_dragStart.X, pos.X);
                var y = Math.Min(_dragStart.Y, pos.Y);
                var w = Math.Abs(pos.X - _dragStart.X);
                var h = Math.Abs(pos.Y - _dragStart.Y);
                _roi = new Rect(x, y, w, h);
                UpdateSelectionVisuals();
            }
        }

        private void OnMouseUp(object? sender, MouseButtonEventArgs e)
        {
            if (_isDragging || _isResizing)
            {
                _isDragging = false;
                _isResizing = false;
                return;
            }

            if (_roi.Width > 10 && _roi.Height > 10)
            {
                _hasSelection = true;
                _resizeHandle.Visibility = Visibility.Visible;
                UpdateSelectionVisuals();
                StartPreview();
            }
            else
            {
                _selectionRect.Visibility = Visibility.Collapsed;
                _resizeHandle.Visibility = Visibility.Collapsed;
                _hasSelection = false;
            }

            _dragStart = default;
        }

        private bool HitResizeHandle(Point pos)
        {
            var handleX = _roi.Right - HandleSize / 2.0;
            var handleY = _roi.Bottom - HandleSize / 2.0;
            return Math.Abs(pos.X - handleX) < HandleSize && Math.Abs(pos.Y - handleY) < HandleSize;
        }

        private void UpdateSelectionVisuals()
        {
            Canvas.SetLeft(_selectionRect, _roi.X);
            Canvas.SetTop(_selectionRect, _roi.Y);
            _selectionRect.Width = _roi.Width;
            _selectionRect.Height = _roi.Height;

            Canvas.SetLeft(_resizeHandle, _roi.Right - HandleSize / 2.0);
            Canvas.SetTop(_resizeHandle, _roi.Bottom - HandleSize / 2.0);
        }

        private void StartPreview()
        {
            _previewTimer?.Dispose();
            _previewTimer = new System.Threading.Timer(async _ =>
            {
                try
                {
                    await Dispatcher.InvokeAsync(() => RunPreviewOcr());
                }
                catch { /* ignore */ }
            }, null, 500, 500);
        }

        private void StopPreview()
        {
            _previewTimer?.Dispose();
            _previewTimer = null;
        }

        private async void RunPreviewOcr()
        {
            if (!_hasSelection || _roi.Width <= 0 || _roi.Height <= 0) return;

            try
            {
                var bitmap = await _screenCapture.CaptureRegionAsync(_roi);
                using var mat = BitmapSourceToMat(bitmap);
                var result = await _ocrEngine.RecognizeAsync(mat, OcrOptions.DigitsAndSeparators);
                var best = result?.BestMatch;

                if (best is not null && !string.IsNullOrEmpty(best.Text))
                {
                    _textValue.Text = best.Text;
                    _confidenceValue.Text = $"confidence: {best.Confidence:F2}";
                }
                else
                {
                    _textValue.Text = "—";
                    _confidenceValue.Text = "confidence: —";
                }
            }
            catch
            {
                _textValue.Text = "(помилка OCR)";
                _confidenceValue.Text = "";
            }
        }

        private static Mat BitmapSourceToMat(BitmapSource bitmap)
        {
            var width = bitmap.PixelWidth;
            var height = bitmap.PixelHeight;
            var stride = width * (bitmap.Format.BitsPerPixel + 7) / 8;
            var pixels = new byte[stride * height];
            bitmap.CopyPixels(pixels, stride, 0);

            unsafe
            {
                fixed (byte* ptr = pixels)
                {
                    using var bgraMat = Mat.FromPixelData(height, width, MatType.CV_8UC4, (IntPtr)ptr, stride);
                    var bgr = new Mat();
                    Cv2.CvtColor(bgraMat, bgr, ColorConversionCodes.BGRA2BGR);
                    return bgr;
                }
            }
        }
    }
}