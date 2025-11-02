using StarCitizenUA.Interfaces;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace StarCitizenUA.Services
{
    public class WindowHelper : IWindowHelper
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public void ApplyWindowRoundCorners(Window window)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;

            int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
            int DWMWCP_ROUND = 2;

            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref DWMWCP_ROUND, sizeof(uint));
        }

        public void HandleMouseMove(Window window, UIElement bgElement, Point pos, FrameworkElement container)
        {
            double centerX = container.ActualWidth / 2;
            double centerY = container.ActualHeight / 2;

            double offsetX = (pos.X - centerX) / centerX;
            double offsetY = (pos.Y - centerY) / centerY;

            double maxSkew = 0.15;

            bgElement.RenderTransform = new SkewTransform(offsetX * maxSkew, offsetY * maxSkew, centerX, centerY);
        }

        public void HandleMouseLeave(Window window, UIElement bgElement, FrameworkElement container)
        {
            bgElement.RenderTransform = new SkewTransform(0, 0, container.ActualWidth / 2, container.ActualHeight / 2);
        }

        public void DragWindow(Window window, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                window.DragMove();
        }
    }
}
