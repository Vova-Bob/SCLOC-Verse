using SCLOCVerse.Interfaces;
using SCLOCVerse.Services.Mining;
using System.Windows;
using System.Windows.Controls;

namespace SCLOCVerse.Controls.SettingsHub
{
    /// <summary>
    /// Settings Hub pane для Signature Scanner:
    /// toggle + Manual ROI calibration + cycle interval slider.
    /// </summary>
    public partial class MiningSettingsPane : UserControl
    {
        private IMiningRecognitionService? _miningRecognition;
        private IMiningOverlayService? _miningOverlay;
        private IPreferencesService? _preferences;
        private IOcrEngine? _ocrEngine;
        private IScreenCaptureService? _screenCapture;
        private bool _isSyncing;

        public MiningSettingsPane()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Інжект сервісів. Викликається з MainWindow при ініціалізації Settings Hub.
        /// </summary>
        public void BindMining(
            IMiningRecognitionService miningRecognition,
            IMiningOverlayService miningOverlay,
            IPreferencesService preferences,
            IOcrEngine ocrEngine,
            IScreenCaptureService screenCapture)
        {
            _miningRecognition = miningRecognition;
            _miningOverlay = miningOverlay;
            _preferences = preferences;
            _ocrEngine = ocrEngine;
            _screenCapture = screenCapture;

            _isSyncing = true;
            try
            {
                EnableMiningCheckBox.IsChecked = preferences.GetMiningEnabled();
                IntervalSlider.Value = preferences.GetMiningCycleIntervalMs();
                UpdateIntervalLabel();

                ManualRoiCheckBox.IsChecked = preferences.GetMiningManualRoiEnabled();
                ShowRoiDebugCheckBox.IsChecked = preferences.GetMiningShowRoiDebug();
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private void EnableMining_Changed(object sender, RoutedEventArgs e)
        {
            if (_isSyncing || _miningRecognition is null || _miningOverlay is null || _preferences is null) return;

            var enabled = EnableMiningCheckBox.IsChecked == true;
            _preferences.SetMiningEnabled(enabled);

            if (enabled)
            {
                _miningRecognition.Enable();
                _miningOverlay.Show();

                // Якщо Manual ROI ввімкнено — застосувати збережені координати.
                if (_preferences.GetMiningManualRoiEnabled())
                {
                    var roi = _preferences.GetMiningRoi();
                    if (roi.Width > 0 && roi.Height > 0)
                    {
                        _miningRecognition.SetManualRoi(roi);
                    }
                }
            }
            else
            {
                _miningRecognition.Disable();
                _miningOverlay.Hide();
            }
        }

        private void ManualRoi_Changed(object sender, RoutedEventArgs e)
        {
            if (_isSyncing || _preferences is null) return;
            var enabled = ManualRoiCheckBox.IsChecked == true;
            _preferences.SetMiningManualRoiEnabled(enabled);

            if (!enabled && _miningRecognition is not null)
            {
                _miningRecognition.ResetManualRoi();
            }
        }

        private void ConfigureRoi_Click(object sender, RoutedEventArgs e)
        {
            if (_ocrEngine is null || _screenCapture is null || _preferences is null) return;

            var window = new RoiCalibrationWindow(_ocrEngine, _screenCapture);
            window.ShowDialog();

            if (window.Result is Rect roi && roi.Width > 10 && roi.Height > 10)
            {
                _preferences.SetMiningRoi(roi);
                _preferences.SetMiningManualRoiEnabled(true);
                ManualRoiCheckBox.IsChecked = true;

                // Застосувати негайно, якщо scanner активний.
                if (_miningRecognition is not null && _miningRecognition.IsEnabled)
                {
                    _miningRecognition.SetManualRoi(roi);
                }
            }
        }

        private void ResetRoi_Click(object sender, RoutedEventArgs e)
        {
            if (_preferences is null) return;

            _preferences.SetMiningManualRoiEnabled(false);
            _preferences.SetMiningRoi(new Rect(0, 0, 0, 0));
            ManualRoiCheckBox.IsChecked = false;

            if (_miningRecognition is not null)
            {
                _miningRecognition.ResetManualRoi();
            }
        }

        private void ShowRoiDebug_Changed(object sender, RoutedEventArgs e)
        {
            if (_isSyncing || _preferences is null) return;
            var enabled = ShowRoiDebugCheckBox.IsChecked == true;
            _preferences.SetMiningShowRoiDebug(enabled);
        }

        private void Interval_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isSyncing || _preferences is null) return;

            var value = (int)IntervalSlider.Value;
            _preferences.SetMiningCycleIntervalMs(value);
            UpdateIntervalLabel();
        }

        private void UpdateIntervalLabel()
        {
            var value = (int)IntervalSlider.Value;
            var hz = 1000.0 / value;
            IntervalValue.Text = $"{value} мс ({hz:F1} Hz)";
        }
    }
}