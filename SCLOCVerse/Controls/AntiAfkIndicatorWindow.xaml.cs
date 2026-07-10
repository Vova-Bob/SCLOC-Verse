using SCLOCVerse.Models.AntiAfk;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SCLOCVerse.Controls
{
    /// <summary>
    /// Overlay-вiкно iндикатора Anti-AFK: пульсуюча точка, click-through,
    /// 5 фiксованих позицiй, 2 анімації (Pulse / Breathing).
    /// Не залежить вiд Hangar Overlay — повнiстю незалежний життєвий цикл.
    /// </summary>
    public partial class AntiAfkIndicatorWindow : Window
    {
        private const double EdgeMargin = 16;

        private const int GwlExStyle = -20;
        private const int WsExLayered = 0x80000;
        private const int WsExTransparent = 0x20;

        private Storyboard? _activeStoryboard;

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

        public AntiAfkIndicatorWindow()
        {
            InitializeComponent();
            SourceInitialized += OnSourceInitialized;
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            ApplyClickThrough();
        }

        /// <summary>
        /// Встановлює WS_EX_TRANSPARENT | WS_EX_LAYERED — кліки проходять крiзь вiкно.
        /// </summary>
        private void ApplyClickThrough()
        {
            var helper = new WindowInteropHelper(this);
            var handle = helper.Handle;
            if (handle == IntPtr.Zero)
                return;

            nint exStyle = GetWindowLong(handle, GwlExStyle);
            exStyle |= (WsExTransparent | WsExLayered);
            SetWindowLong(handle, GwlExStyle, exStyle);
        }

        // ===== Оновлення вигляду =====

        /// <summary>
        /// Встановлює колір індикатора. "Hidden" → вiкно приховується.
        /// </summary>
        public void UpdateColor(string colorValue)
        {
            if (colorValue == "Hidden")
            {
                Visibility = Visibility.Hidden;
                return;
            }

            try
            {
                var color = (Color)ColorConverter.ConvertFromString(colorValue);
                IndicatorDot.Fill = new SolidColorBrush(color);
            }
            catch
            {
                // Невалiдний hex — зеленый за замовчуванням.
                IndicatorDot.Fill = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
            }
        }

        /// <summary>
        /// Встановлює розмiр iндикатора (дiаметр точки в пiкселях) та перепозицiйовує вiкно.
        /// </summary>
        public void UpdateSize(double size, AntiAfkIndicatorPosition position)
        {
            Width = size;
            Height = size;
            IndicatorDot.Width = size;
            IndicatorDot.Height = size;
            UpdatePosition(position, size);
        }

        /// <summary>
        /// Розмiщує вiкно за обраною позицiєю з урахуванням розмiру.
        /// </summary>
        public void UpdatePosition(AntiAfkIndicatorPosition position, double size)
        {
            var screen = SystemParameters.WorkArea;
            double w = screen.Width;
            double h = screen.Height;

            Left = position switch
            {
                AntiAfkIndicatorPosition.TopLeft => EdgeMargin,
                AntiAfkIndicatorPosition.TopRight => w - size - EdgeMargin,
                AntiAfkIndicatorPosition.BottomLeft => EdgeMargin,
                AntiAfkIndicatorPosition.BottomRight => w - size - EdgeMargin,
                AntiAfkIndicatorPosition.Center => (w - size) / 2,
                _ => w - size - EdgeMargin
            };

            Top = position switch
            {
                AntiAfkIndicatorPosition.TopLeft => EdgeMargin,
                AntiAfkIndicatorPosition.TopRight => EdgeMargin,
                AntiAfkIndicatorPosition.BottomLeft => h - size - EdgeMargin,
                AntiAfkIndicatorPosition.BottomRight => h - size - EdgeMargin,
                AntiAfkIndicatorPosition.Center => (h - size) / 2,
                _ => EdgeMargin
            };
        }

        // ===== Анімація =====

        /// <summary>
        /// Запускає обрану анімацiю пульсації.
        /// </summary>
        public void StartAnimation(AntiAfkIndicatorAnimation animation)
        {
            StopAnimation();

            string key = animation == AntiAfkIndicatorAnimation.Breathing
                ? "BreathingAnimation"
                : "PulseAnimation";

            _activeStoryboard = (Storyboard)FindResource(key);
            _activeStoryboard.Begin();
        }

        /// <summary>
        /// Зупиняє поточну анімацiю.
        /// </summary>
        public void StopAnimation()
        {
            if (_activeStoryboard == null)
                return;

            _activeStoryboard.Stop();
            _activeStoryboard = null;
            IndicatorDot.Opacity = 1.0;
        }
    }
}
