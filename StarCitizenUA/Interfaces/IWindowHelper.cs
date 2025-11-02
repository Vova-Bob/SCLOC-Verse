using System.Windows;
using System.Windows.Input;

namespace StarCitizenUA.Interfaces
{
    public interface IWindowHelper
    {
        void ApplyWindowRoundCorners(Window window);
        void HandleMouseMove(Window window, UIElement bgElement, Point position, FrameworkElement container);
        void HandleMouseLeave(Window window, UIElement bgElement, FrameworkElement container);
        void DragWindow(Window window, MouseButtonEventArgs e);
    }
}
