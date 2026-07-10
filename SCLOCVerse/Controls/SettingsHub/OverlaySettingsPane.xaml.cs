using System.Windows;
using System.Windows.Controls;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Services.HangarTimer;

namespace SCLOCVerse.Controls.SettingsHub
{
    /// <summary>
    /// Панель «Overlay» Settings Hub.
    /// Редагує персистентні налаштування оверлея Hangar Timer (масштаб/прозорість)
    /// через IHangarSettingsService. Позиція — лише відображення (службова).
    /// Зміни застосовуються негайно до відкритого overlay (live-preview) або
    /// при наступному показі.
    /// </summary>
    public partial class OverlaySettingsPane : UserControl
    {
        private const double DefaultScale = 0.6;
        private const double DefaultOpacity = 0.92;

        private IHangarSettingsService? _settings;
        private IHangarOverlayService? _overlay;
        private bool _isInitializing;

        public OverlaySettingsPane()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Завантажує поточні значення з сервісів та підписується на зміни.
        /// </summary>
        /// <param name="settings">Персистентні налаштування (scale/opacity/position).</param>
        /// <param name="overlay">Overlay-сервіс для live-preview (можна null).</param>
        public void Bind(IHangarSettingsService settings, IHangarOverlayService overlay)
        {
            _settings = settings;
            _overlay = overlay;

            var scale = settings.GetOverlayScale();
            var opacity = settings.GetOverlayOpacity();

            _isInitializing = true;
            try
            {
                ScaleSlider.Value = scale;
                OpacitySlider.Value = opacity;
                ScaleValue.Text = scale.ToString("0.00");
                OpacityValue.Text = opacity.ToString("0.00");
                PosXBox.Text = settings.GetOverlayX().ToString("0");
                PosYBox.Text = settings.GetOverlayY().ToString("0");
            }
            finally
            {
                _isInitializing = false;
            }

            ScaleSlider.ValueChanged += ScaleSlider_ValueChanged;
            OpacitySlider.ValueChanged += OpacitySlider_ValueChanged;
            ResetButton.Click += ResetButton_Click;
        }

        private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing || _settings is null)
                return;

            _settings.SetOverlayScale(e.NewValue);
            ScaleValue.Text = e.NewValue.ToString("0.00");

            // Live-preview: оновити відкритий overlay негайно.
            if (_overlay?.IsOpen == true && _overlay is HangarOverlayService svc)
                svc.ApplyScale(e.NewValue);
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing || _settings is null)
                return;

            _settings.SetOverlayOpacity(e.NewValue);
            OpacityValue.Text = e.NewValue.ToString("0.00");

            // Live-preview: оновити відкритий overlay негайно.
            if (_overlay?.IsOpen == true && _overlay is HangarOverlayService svc)
                svc.ApplyOpacity(e.NewValue);
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_settings is null)
                return;

            _isInitializing = true;
            try
            {
                _settings.SetOverlayScale(DefaultScale);
                _settings.SetOverlayOpacity(DefaultOpacity);
                ScaleSlider.Value = DefaultScale;
                OpacitySlider.Value = DefaultOpacity;
                ScaleValue.Text = DefaultScale.ToString("0.00");
                OpacityValue.Text = DefaultOpacity.ToString("0.00");

                // Live-preview reset.
                if (_overlay?.IsOpen == true && _overlay is HangarOverlayService svc)
                {
                    svc.ApplyScale(DefaultScale);
                    svc.ApplyOpacity(DefaultOpacity);
                }
            }
            finally
            {
                _isInitializing = false;
            }
        }
    }
}
