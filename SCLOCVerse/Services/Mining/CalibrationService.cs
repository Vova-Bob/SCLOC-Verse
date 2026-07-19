using SCLOCVerse.Interfaces;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SCLOCVerse.Services.Mining
{
    /// <summary>
    /// MVP Calibration Tool: full-screen overlay для drag-select регіону екрана.
    /// Користувач затискає ЛКМ → тягне → відпускає → отримуємо прямокутник.
    /// </summary>
    public sealed class CalibrationService
    {
        /// <summary>
        /// Показати full-screen overlay та дочекатися вибору регіону.
        /// Повертає null якщо користувач скасував (Esc або клік без drag).
        /// </summary>
        public Rect? SelectRegion(string prompt = "Виділіть регіон для OCR")
        {
            var screen = SystemParameters.WorkArea;
            Rect? selected = null;
            var done = new ManualResetEventSlim(false);

            // Full-screen window — transparent, topmost.
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)), // напівпрозорий темний
                Topmost = true,
                ShowInTaskbar = false,
                Width = screen.Width,
                Height = screen.Height,
                Left = 0,
                Top = 0,
                ShowActivated = true,
                Cursor = Cursors.Cross
            };

            // Інструкція + selection rect — у Grid.
            var selectionRect = new System.Windows.Shapes.Rectangle
            {
                Stroke = Brushes.Cyan,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(30, 0, 255, 255)),
                Visibility = Visibility.Collapsed
            };
            var instruction = new System.Windows.Controls.TextBlock
            {
                Text = prompt + "\n\nЛКМ + тягнути = виділити\nEsc = скасувати",
                FontSize = 18,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var grid = new System.Windows.Controls.Grid();
            grid.Children.Add(instruction);
            grid.Children.Add(selectionRect);
            window.Content = grid;

            Point? startPoint = null;

            // Mouse handlers.
            window.MouseLeftButtonDown += (_, e) =>
            {
                startPoint = e.GetPosition(window);
                selectionRect.Visibility = Visibility.Visible;
            };

            window.MouseMove += (_, e) =>
            {
                if (startPoint is null) return;
                var current = e.GetPosition(window);
                var x = Math.Min(startPoint.Value.X, current.X);
                var y = Math.Min(startPoint.Value.Y, current.Y);
                var w = Math.Abs(current.X - startPoint.Value.X);
                var h = Math.Abs(current.Y - startPoint.Value.Y);
                selectionRect.Width = w;
                selectionRect.Height = h;
                var rectTransform = new TranslateTransform(x, y);
                selectionRect.RenderTransform = rectTransform;
            };

            window.MouseLeftButtonUp += (_, e) =>
            {
                if (startPoint is null)
                {
                    done.Set();
                    return;
                }

                var current = e.GetPosition(window);
                var x = Math.Min(startPoint.Value.X, current.X);
                var y = Math.Min(startPoint.Value.Y, current.Y);
                var w = Math.Abs(current.X - startPoint.Value.X);
                var h = Math.Abs(current.Y - startPoint.Value.Y);

                if (w > 5 && h > 5) // мінімальний розмір
                {
                    selected = new Rect(x, y, w, h);
                }
                done.Set();
            };

            window.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    selected = null;
                    done.Set();
                }
            };

            window.Show();
            done.Wait();
            window.Close();

            return selected;
        }
    }
}