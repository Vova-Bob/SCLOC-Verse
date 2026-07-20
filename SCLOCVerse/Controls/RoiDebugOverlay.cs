using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SCLOCVerse.Controls
{
    /// <summary>
    /// Debug overlay — показує рамку ROI області Signature Scanner.
    /// Click-through (WS_EX_TRANSPARENT | WS_EX_LAYERED), Topmost.
    /// Не перехоплює клавіатуру/мишу.
    /// </summary>
    public sealed class RoiDebugOverlay : Window
    {
        private const int GwlExStyle = -20;
        private const int WsExLayered = 0x80000;
        private const int WsExTransparent = 0x20;

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern nint GetWindowLong64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern nint SetWindowLong64(IntPtr hWnd, int nIndex, nint dwNewLong);

        private static nint GetWindowLong(IntPtr hWnd, int nIndex)
            => Environment.Is64BitProcess ? GetWindowLong64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);

        private static void SetWindowLong(IntPtr hWnd, int nIndex, nint value)
        {
            if (Environment.Is64BitProcess)
                SetWindowLong64(hWnd, nIndex, value);
            else
                SetWindowLong32(hWnd, nIndex, (int)value);
        }

        private readonly Rectangle _border;

        public RoiDebugOverlay()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight;

            _border = new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromArgb(200, 0, 255, 255)),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 2 }
            };
            Content = _border;
            SourceInitialized += (_, _) => EnableClickThrough();
        }

        /// <summary>Показати рамку у вказаній області екрана.</summary>
        public void ShowRoi(Rect roi)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<Rect>(ShowRoi), roi);
                return;
            }

            Width = roi.Width;
            Height = roi.Height;
            Left = roi.X;
            Top = roi.Y;

            if (!IsVisible)
                Show();
        }

        /// <summary>Приховати рамку.</summary>
        public void HideRoi()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(HideRoi));
                return;
            }

            if (IsVisible)
                Hide();
        }

        private void EnableClickThrough()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var extended = GetWindowLong(hwnd, GwlExStyle);
            SetWindowLong(hwnd, GwlExStyle, new IntPtr(extended | WsExLayered | WsExTransparent));
        }
    }
}