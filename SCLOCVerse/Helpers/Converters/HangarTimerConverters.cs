using SCLOCVerse.Models.HangarTimer;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SCLOCVerse.Helpers.Converters
{
    /// <summary>
    /// Конвертер фази циклу у фоновий колір.
    /// </summary>
    [ValueConversion(typeof(HangarCyclePhase), typeof(Brush))]
    public class PhaseToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is HangarCyclePhase phase)
            {
                return phase switch
                {
                    HangarCyclePhase.Open => new SolidColorBrush(Color.FromArgb(190, 35, 120, 35)),
                    HangarCyclePhase.Closed => new SolidColorBrush(Color.FromArgb(190, 120, 35, 35)),
                    HangarCyclePhase.Resetting => new SolidColorBrush(Color.FromArgb(200, 120, 120, 20)),
                    _ => new SolidColorBrush(Color.FromArgb(190, 80, 80, 80))
                };
            }
            return new SolidColorBrush(Color.FromArgb(190, 80, 80, 80));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Конвертер фази циклу у колір рамки.
    /// </summary>
    [ValueConversion(typeof(HangarCyclePhase), typeof(Brush))]
    public class PhaseToBorderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is HangarCyclePhase phase)
            {
                return phase switch
                {
                    HangarCyclePhase.Open => new SolidColorBrush(Color.FromArgb(240, 160, 90, 0)),
                    HangarCyclePhase.Closed => new SolidColorBrush(Color.FromArgb(240, 60, 60, 60)),
                    HangarCyclePhase.Resetting => new SolidColorBrush(Color.FromArgb(240, 160, 80, 0)),
                    _ => new SolidColorBrush(Color.FromArgb(240, 100, 100, 100))
                };
            }
            return new SolidColorBrush(Color.FromArgb(240, 100, 100, 100));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Конвертер фази циклу у колір тексту статусу.
    /// </summary>
    [ValueConversion(typeof(HangarCyclePhase), typeof(Brush))]
    public class PhaseToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is HangarCyclePhase phase)
            {
                return phase switch
                {
                    HangarCyclePhase.Open => new SolidColorBrush(Color.FromArgb(255, 220, 140, 220)),
                    HangarCyclePhase.Closed => new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)),
                    HangarCyclePhase.Resetting => new SolidColorBrush(Color.FromArgb(255, 200, 80, 0)),
                    _ => new SolidColorBrush(Color.FromArgb(255, 180, 180, 180))
                };
            }
            return new SolidColorBrush(Color.FromArgb(255, 180, 180, 180));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Конвертер стану LED у колір заливки.
    /// </summary>
    [ValueConversion(typeof(HangarLightState), typeof(Brush))]
    public class LightStateToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is HangarLightState state)
            {
                return state switch
                {
                    HangarLightState.Red => new SolidColorBrush(Color.FromRgb(255, 80, 80)),
                    HangarLightState.Green => new SolidColorBrush(Color.FromRgb(80, 200, 80)),
                    HangarLightState.Black => new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                    _ => new SolidColorBrush(Colors.Gray)
                };
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Конвертер: пустий рядок → Collapsed, непустий → Visible.
    /// </summary>
    [ValueConversion(typeof(string), typeof(Visibility))]
    public class EmptyStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Конвертер: null → false, не null → true.
    /// </summary>
    [ValueConversion(typeof(object), typeof(bool))]
    public class NullToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
