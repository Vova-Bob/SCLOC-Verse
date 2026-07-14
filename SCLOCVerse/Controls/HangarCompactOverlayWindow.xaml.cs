using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.HangarTimer;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace SCLOCVerse.Controls
{
    /// <summary>
    /// Компактне overlay-вікно Hangar Timer (спрощений вигляд).
    /// Самостійний бейдж: 5 LED + таймер, прозорий фон, SizeToContent.
    /// Не має card-контейнера — лише тонка округла оболонка навколо бейджа.
    /// Незалежний життєвий цикл, але ті ж налаштування (позиція/прозорість/масштаб).
    /// </summary>
    public partial class HangarCompactOverlayWindow : Window, IHangarOverlayWindow
    {
        private const int GwlExStyle = -20;
        private const int WsExLayered = 0x80000;
        private const int WsExTransparent = 0x20;

        private readonly HangarTimerState _state;
        private readonly IHangarSettingsService _settingsService;
        private readonly Action _onClose;

        private bool _clickThrough;
        private bool _clickThroughTemp;
        private Point? _dragStart;
        private double _scale;

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

        public HangarCompactOverlayWindow(HangarTimerState state, IHangarSettingsService settingsService, Action onClose)
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
        }

        // ===== IHangarOverlayWindow (специфічні методи) =====

        /// <summary>
        /// У компактному вигляді підказка гарячих клавіш не показується.
        /// Метод існує для сумісності з контрактом.
        /// </summary>
        public void SetHotkeyHint(string text)
        {
            // Нічого не робимо — у спрощеному вигляді підказки немає.
        }

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

        // ===== Lifecycle =====

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
                ApplyScale();
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

        // ===== Persist + Scale =====

        private void LoadPersistedState()
        {
            var x = _settingsService.GetOverlayX();
            var y = _settingsService.GetOverlayY();
            _scale = _settingsService.GetOverlayScale();
            var opacity = _settingsService.GetOverlayOpacity();

            _state.Scale = _scale;
            _state.Opacity = opacity;

            Left = x;
            Top = y;
            Opacity = opacity;
            ApplyScale();
            ClampPosition();
        }

        private void SavePersistedState()
        {
            _settingsService.SetOverlayPosition(Left, Top);
            _settingsService.SetOverlayScale(_state.Scale);
            _settingsService.SetOverlayOpacity(_state.Opacity);
        }

        /// <summary>
        /// Масштабування компактного бейджа через LayoutTransform.
        /// SizeToContent лишається коректним — бейдж підлаштовується під вміст,
        /// а LayoutTransform масштабує вже зібраний бейдж.
        /// </summary>
        private void ApplyScale()
        {
            _scale = _state.Scale;
            Badge.LayoutTransform = new ScaleTransform(_scale, _scale);
            ClampPosition();
        }

        private void ClampPosition()
        {
            var screen = SystemParameters.WorkArea;
            Left = Math.Max(screen.Left, Math.Min(screen.Right - ActualWidth, Left));
            Top = Math.Max(screen.Top, Math.Min(screen.Bottom - ActualHeight, Top));
        }
    }
}