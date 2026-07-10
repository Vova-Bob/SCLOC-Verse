using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.HangarTimer;
using SCLOCVerse.Services.HangarTimer;

namespace SCLOCVerse.Controls.SettingsHub
{
    /// <summary>
    /// Панель «Overlay» Settings Hub.
    /// Редагує налаштування оверлея Hangar Timer (масштаб/прозорість/позиція)
    /// через IHangarSettingsService + IHangarOverlayService.
    /// Single Source of Truth — HangarTimerState. Будь-яка зміна (слайдер,
    /// гарячі клавіші, програмно) синхронізується через state.PropertyChanged.
    /// </summary>
    public partial class OverlaySettingsPane : UserControl
    {
        private const double DefaultScale = 0.6;
        private const double DefaultOpacity = 0.92;
        private const double DefaultPosX = 20.0;
        private const double DefaultPosY = 20.0;

        private IHangarSettingsService? _settings;
        private HangarOverlayService? _overlay;
        private HangarTimerState? _state;

        /// <summary>true під час програмної зміни слайдерів (уникнення зациклення).</summary>
        private bool _isSyncing;

        public OverlaySettingsPane()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Завантажує поточні значення з сервісів та встановлює двосторонню синхронізацію.
        /// </summary>
        /// <param name="settings">Персистентні налаштування (scale/opacity/position).</param>
        /// <param name="overlay">Overlay-сервіс для live-preview + state-синхронізації.</param>
        public void Bind(IHangarSettingsService settings, IHangarOverlayService overlay)
        {
            _settings = settings;

            // Отримуємо concrete HangarOverlayService для доступу до state (SSOT).
            _overlay = overlay as HangarOverlayService;
            _state = _overlay?.State;

            var scale = settings.GetOverlayScale();
            var opacity = settings.GetOverlayOpacity();
            var x = settings.GetOverlayX();
            var y = settings.GetOverlayY();

            _isSyncing = true;
            try
            {
                ScaleSlider.Value = scale;
                OpacitySlider.Value = opacity;
                ScaleValue.Text = scale.ToString("0.00");
                OpacityValue.Text = opacity.ToString("0.00");
                PosXBox.Text = x.ToString("0");
                PosYBox.Text = y.ToString("0");
            }
            finally
            {
                _isSyncing = false;
            }

            ScaleSlider.ValueChanged += ScaleSlider_ValueChanged;
            OpacitySlider.ValueChanged += OpacitySlider_ValueChanged;
            ScaleSlider.PreviewMouseLeftButtonDown += Slider_PreviewMouseLeftButtonDown;
            OpacitySlider.PreviewMouseLeftButtonDown += Slider_PreviewMouseLeftButtonDown;
            ResetButton.Click += ResetButton_Click;

            PosXBox.LostFocus += PosBox_LostFocus;
            PosYBox.LostFocus += PosBox_LostFocus;
            PosXBox.PreviewKeyDown += PosBox_PreviewKeyDown;
            PosYBox.PreviewKeyDown += PosBox_PreviewKeyDown;

            // Bidirectional sync: хоткеї/програмні зміни → слайдери оновлюються.
            if (_state != null)
                _state.PropertyChanged += State_PropertyChanged;

            // Drag sync: переміщення overlay → поля X/Y оновлюються.
            if (_overlay != null)
                _overlay.PositionChanged += Overlay_PositionChanged;
        }

        // ============ Jump-to-click: клік по доріжці = точна позиція ============

        private void Slider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Slider slider)
                return;

            // Клік по Thumb — не втручаємось (drag працює штатно).
            if (IsDescendantOf(e.OriginalSource as DependencyObject, typeof(Thumb)))
                return;

            var track = slider.Template.FindName("PART_Track", slider) as Track;
            if (track is null || track.ActualWidth <= 0)
                return;

            double pct = e.GetPosition(track).X / track.ActualWidth;
            double value = slider.Minimum + pct * (slider.Maximum - slider.Minimum);
            slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);
            e.Handled = true;
        }

        private static bool IsDescendantOf(DependencyObject? element, Type ancestorType)
        {
            while (element != null)
            {
                if (element.GetType() == ancestorType || element.GetType().IsSubclassOf(ancestorType))
                    return true;
                element = VisualTreeHelper.GetParent(element);
            }
            return false;
        }

        // ============ Слайдер → State + Settings (з live-preview) ============

        private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isSyncing || _settings is null)
                return;

            _settings.SetOverlayScale(e.NewValue);
            ScaleValue.Text = e.NewValue.ToString("0.00");

            // Оновити SSOT → overlay реагує + запобігає зацикленню через _isSyncing.
            _isSyncing = true;
            try
            {
                if (_state != null)
                    _state.Scale = e.NewValue;
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isSyncing || _settings is null)
                return;

            _settings.SetOverlayOpacity(e.NewValue);
            OpacityValue.Text = e.NewValue.ToString("0.00");

            _isSyncing = true;
            try
            {
                if (_state != null)
                    _state.Opacity = e.NewValue;
            }
            finally
            {
                _isSyncing = false;
            }
        }

        // ============ State → Слайдери (хоткеї → UI) ============

        private void State_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isSyncing || _state is null)
                return;

            // Можливо викликається не з UI-потоку — маршалимо.
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => State_PropertyChanged(sender, e));
                return;
            }

            _isSyncing = true;
            try
            {
                if (e.PropertyName == nameof(HangarTimerState.Scale))
                {
                    ScaleSlider.Value = _state.Scale;
                    ScaleValue.Text = _state.Scale.ToString("0.00");
                }
                else if (e.PropertyName == nameof(HangarTimerState.Opacity))
                {
                    OpacitySlider.Value = _state.Opacity;
                    OpacityValue.Text = _state.Opacity.ToString("0.00");
                }
            }
            finally
            {
                _isSyncing = false;
            }
        }

        // ============ Drag → Settings Hub (позиція) ============

        private void Overlay_PositionChanged(object? sender, (double X, double Y) pos)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => Overlay_PositionChanged(sender, pos));
                return;
            }

            // Оновити поля лише якщо користувач не редагує їх зараз.
            if (!PosXBox.IsFocused)
                PosXBox.Text = pos.X.ToString("0");
            if (!PosYBox.IsFocused)
                PosYBox.Text = pos.Y.ToString("0");

            // Персистити нову позицію.
            _settings?.SetOverlayPosition(pos.X, pos.Y);
        }

        // ============ Позиція (Bug 1 — редаговна + live-apply) ============

        private void PosBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyPosition();
        }

        private void PosBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyPosition();
                e.Handled = true;
            }
        }

        private void ApplyPosition()
        {
            if (_settings is null)
                return;

            if (!double.TryParse(PosXBox.Text, out double x))
            {
                // Невалідне значення — відновити з settings.
                PosXBox.Text = _settings.GetOverlayX().ToString("0");
                return;
            }

            if (!double.TryParse(PosYBox.Text, out double y))
            {
                PosYBox.Text = _settings.GetOverlayY().ToString("0");
                return;
            }

            _settings.SetOverlayPosition(x, y);

            // Live-preview: застосувати до відкритого overlay.
            if (_overlay?.IsOpen == true)
            {
                var window = _overlay.GetWindow();
                if (window != null)
                {
                    window.Left = x;
                    window.Top = y;
                }
            }
        }

        // ============ Reset ============

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_settings is null)
                return;

            _isSyncing = true;
            try
            {
                _settings.SetOverlayScale(DefaultScale);
                _settings.SetOverlayOpacity(DefaultOpacity);
                _settings.SetOverlayPosition(DefaultPosX, DefaultPosY);
                ScaleSlider.Value = DefaultScale;
                OpacitySlider.Value = DefaultOpacity;
                ScaleValue.Text = DefaultScale.ToString("0.00");
                OpacityValue.Text = DefaultOpacity.ToString("0.00");
                PosXBox.Text = DefaultPosX.ToString("0");
                PosYBox.Text = DefaultPosY.ToString("0");

                if (_state != null)
                {
                    _state.Scale = DefaultScale;
                    _state.Opacity = DefaultOpacity;
                }

                // Live-preview позиції.
                if (_overlay?.IsOpen == true)
                {
                    var window = _overlay.GetWindow();
                    if (window != null)
                    {
                        window.Left = DefaultPosX;
                        window.Top = DefaultPosY;
                    }
                }
            }
            finally
            {
                _isSyncing = false;
            }
        }
    }
}
