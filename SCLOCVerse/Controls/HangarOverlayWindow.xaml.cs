using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.HangarTimer;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace SCLOCVerse.Controls
{
    /// <summary>
    /// WPF overlay вікно Hangar Timer. Code-behind містить лише WinAPI, drag, життєвий цикл вікна
    /// та координати/розміри, що відповідають оригінальному WinForms Overlay.
    /// </summary>
    public partial class HangarOverlayWindow : Window
    {
        // ---- Base canvas size (1:1 з оригінальним WinForms) ----
        private const double BaseWidth = 820;
        private const double BaseHeight = 280;
        private const double CardMargin = 12;
        private const double CardCornerRadius = 14;

        // ---- LED ----
        private const int LedCount = 5;
        private const double LedDiameter = 32;
        private const double LedSpacing = 30;
        private const double LedTop = 146;
        private const double LedLabelTop = 190;

        // ---- Text positions ----
        private const double StatusTop = 28;
        private const double TimerTop = 82;
        private const double HotkeysLeft = 16;
        private const double HotkeysTop = 256;

        // ---- Win32 ----
        private const int GwlExStyle = -20;
        private const int WsExLayered = 0x80000;
        private const int WsExTransparent = 0x20;

        private readonly HangarTimerState _state;
        private readonly IHangarSettingsService _settingsService;
        private readonly Action _onClose;

        private bool _clickThrough;
        private bool _clickThroughTemp;
        private Point? _dragStart;

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern nint GetWindowLong64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern nint SetWindowLong64(IntPtr hWnd, int nIndex, nint dwNewLong);

        private static nint GetWindowLong(IntPtr hWnd, int nIndex)
            => Environment.Is64BitProcess ? GetWindowLong64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);

        private static void SetWindowLong(IntPtr hWnd, int nIndex, nint value)
        {
            if (Environment.Is64BitProcess)
                SetWindowLong64(hWnd, nIndex, value);
            else
                SetWindowLong32(hWnd, nIndex, (int)value);
        }

        public HangarOverlayWindow(HangarTimerState state, IHangarSettingsService settingsService, Action onClose)
        {
            _state = state;
            _settingsService = settingsService;
            _onClose = onClose;

            InitializeComponent();
            DataContext = _state;

            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
            Closing += OnClosing;
            KeyDown += OnKeyDown;

            MouseLeftButtonDown += OnMouseLeftButtonDown;
            MouseMove += OnMouseMove;
            MouseLeftButtonUp += OnMouseLeftButtonUp;

            _state.PropertyChanged += OnStatePropertyChanged;

            InitializeLedPositions();
        }

        private void InitializeLedPositions()
        {
            double rowWidth = LedCount * LedDiameter + (LedCount - 1) * LedSpacing;
            double startX = (BaseWidth - rowWidth) / 2;

            for (int i = 0; i < _state.Lights.Length; i++)
            {
                var light = _state.Lights[i];
                double left = startX + i * (LedDiameter + LedSpacing);
                light.Left = left;
                light.Top = LedTop;
                light.LabelLeft = left - 14;
            }
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            helper.EnsureHandle();
            ApplyClickThrough(_clickThrough);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadPersistedState();
        }

        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Відписуємося від long-lived стану до того, як вікно стане недоступним.
            // Інакше _state.PropertyChanged утримуватиме закрите вікно від посилання (витік).
            _state.PropertyChanged -= OnStatePropertyChanged;

            SavePersistedState();
            _onClose();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private void OnStatePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(HangarTimerState.Opacity))
                Opacity = _state.Opacity;

            if (e.PropertyName == nameof(HangarTimerState.Scale))
                ApplyWindowScale();
        }

        // ===== Drag =====

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(this);
            CaptureMouse();
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragStart.HasValue || e.LeftButton != MouseButtonState.Pressed)
                return;

            var pos = e.GetPosition(this);
            var delta = pos - _dragStart.Value;
            Left += delta.X;
            Top += delta.Y;
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _dragStart = null;
            ReleaseMouseCapture();
            EndTemporaryDragMode();
            SavePersistedState();
        }

        // ===== Click-through =====

        public void ToggleClickThrough()
        {
            _clickThrough = !_clickThrough;
            _clickThroughTemp = false;
            ApplyClickThrough(_clickThrough);
        }

        public void BeginTemporaryDragMode()
        {
            if (_clickThrough && !_clickThroughTemp)
            {
                _clickThroughTemp = true;
                ApplyClickThrough(false);
            }
        }

        public void EndTemporaryDragMode()
        {
            if (_clickThroughTemp)
            {
                _clickThroughTemp = false;
                ApplyClickThrough(true);
            }
        }

        private void ApplyClickThrough(bool enabled)
        {
            var helper = new WindowInteropHelper(this);
            var handle = helper.Handle;
            if (handle == IntPtr.Zero)
                return;

            nint exStyle = GetWindowLong(handle, GwlExStyle);
            if (enabled)
                exStyle |= (WsExTransparent | WsExLayered);
            else
                exStyle = (exStyle | WsExLayered) & ~WsExTransparent;

            SetWindowLong(handle, GwlExStyle, exStyle);
        }

        // ===== Persist =====

        private void LoadPersistedState()
        {
            var x = _settingsService.GetOverlayX();
            var y = _settingsService.GetOverlayY();
            var scale = _settingsService.GetOverlayScale();
            var opacity = _settingsService.GetOverlayOpacity();

            _state.Scale = scale;
            _state.Opacity = opacity;

            Left = x;
            Top = y;
            ApplyWindowScale();
            ClampPosition();
        }

        private void SavePersistedState()
        {
            _settingsService.SetOverlayPosition(Left, Top);
            _settingsService.SetOverlayScale(_state.Scale);
            _settingsService.SetOverlayOpacity(_state.Opacity);
        }

        private void ApplyWindowScale()
        {
            Width = BaseWidth * _state.Scale;
            Height = BaseHeight * _state.Scale;
        }

        private void ClampPosition()
        {
            var screen = SystemParameters.WorkArea;
            Left = Math.Max(screen.Left, Math.Min(screen.Right - Width, Left));
            Top = Math.Max(screen.Top, Math.Min(screen.Bottom - Height, Top));
        }
    }
}
