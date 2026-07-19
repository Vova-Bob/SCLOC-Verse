using SCLOCVerse.Interfaces;
using System.Windows;
using System.Windows.Controls;

namespace SCLOCVerse.Controls.SettingsHub
{
    /// <summary>
    /// Settings Hub pane для Mining Module: toggle + cycle interval slider.
    /// Bind через <see cref="BindMining(IMiningRecognitionService, IMiningOverlayService, IPreferencesService)"/>.
    /// </summary>
    public partial class MiningSettingsPane : UserControl
    {
        private IMiningRecognitionService? _miningRecognition;
        private IMiningOverlayService? _miningOverlay;
        private IPreferencesService? _preferences;
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
            IPreferencesService preferences)
        {
            _miningRecognition = miningRecognition;
            _miningOverlay = miningOverlay;
            _preferences = preferences;

            _isSyncing = true;
            try
            {
                EnableMiningCheckBox.IsChecked = preferences.GetMiningEnabled();
                IntervalSlider.Value = preferences.GetMiningCycleIntervalMs();
                UpdateIntervalLabel();
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
            }
            else
            {
                _miningRecognition.Disable();
                _miningOverlay.Hide();
            }
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