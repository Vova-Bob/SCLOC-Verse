using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SCLOCVerse.Helpers.Converters
{
    /// <summary>
    /// Вибирає стиль кнопки картки залежно від наявності LaunchAction.
    /// </summary>
    [ValueConversion(typeof(Action), typeof(Style))]
    public class ActionToButtonStyleConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var action = value as Action;
            var canvas = parameter as FrameworkElement;
            if (canvas == null)
                return null;

            return action != null
                ? canvas.FindResource("ToolCardButton") as Style
                : canvas.FindResource("ToolPlaceholderButton") as Style;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
