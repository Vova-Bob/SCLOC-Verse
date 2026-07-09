using System.Windows;
using System.Windows.Controls;
using SCLOCVerse.Interfaces;

namespace SCLOCVerse.Controls.SettingsHub
{
    /// <summary>
    /// Панель «Overlay» Settings Hub.
    /// Редагує персистентні налаштування оверлея Hangar Timer (масштаб/прозорість)
    /// через IHangarSettingsService. Позиція — лише відображення (службова).
    /// Зміни застосовуються при наступному показі оверлея.
    /// </summary>
    public partial class OverlaySettingsPane : UserControl
    {
        private const double DefaultScale = 0.6;
        private const double DefaultOpacity = 0.92;

        private IHangarSettingsService? _settings;
        private bool _isInitializing;

        public OverlaySettingsPane()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Завантажує поточні значення з IHangarSettingsService та підписується на зміни.
        /// </summary>
        public void Bind(IHangarSettingsService settings)
        {
            _settings = settings;

            _isInitializing = true;
            try
            {
                ScaleSlider.Value = settings.GetOverlayScale();
                OpacitySlider.Value = settings.GetOverlayOpacity();
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
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing || _settings is null)
                return;

            _settings.SetOverlayOpacity(e.NewValue);
            OpacityValue.Text = e.NewValue.ToString("0.00");
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
            }
            finally
            {
                _isInitializing = false;
            }
        }
    }
}
