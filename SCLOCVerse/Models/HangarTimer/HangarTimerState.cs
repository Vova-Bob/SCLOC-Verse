using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SCLOCVerse.Models.HangarTimer
{
    /// <summary>
    /// Стан overlay Hangar Timer для прив'язки до WPF.
    /// </summary>
    public sealed class HangarTimerState : INotifyPropertyChanged
    {
        private HangarCyclePhase _phase;
        private string _statusMessage = string.Empty;
        private string _statusLine = string.Empty;
        private string _timerText = string.Empty;
        private double _opacity;
        private double _scale;

        public HangarCyclePhase Phase
        {
            get => _phase;
            set => Set(ref _phase, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => Set(ref _statusMessage, value);
        }

        public string StatusLine
        {
            get => _statusLine;
            set => Set(ref _statusLine, value);
        }

        public string TimerText
        {
            get => _timerText;
            set => Set(ref _timerText, value);
        }

        public double Opacity
        {
            get => _opacity;
            set => Set(ref _opacity, value);
        }

        public double Scale
        {
            get => _scale;
            set => Set(ref _scale, value);
        }

        public HangarLightViewModel[] Lights { get; } = new HangarLightViewModel[5];

        public HangarTimerState()
        {
            for (int i = 0; i < Lights.Length; i++)
                Lights[i] = new HangarLightViewModel();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Модель одного LED індикатора.
    /// </summary>
    public sealed class HangarLightViewModel : INotifyPropertyChanged
    {
        private HangarLightState _state;
        private string _label = string.Empty;

        public HangarLightState State
        {
            get => _state;
            set => Set(ref _state, value);
        }

        public string Label
        {
            get => _label;
            set => Set(ref _label, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
