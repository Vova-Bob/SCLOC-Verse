using SCLOCVerse.Models.AutoKey;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SCLOCVerse.Controls
{
    /// <summary>
    /// Overlay-вікно індикатора Auto Key: компактний badge «● Auto Key»,
    /// click-through (WS_EX_TRANSPARENT | WS_EX_LAYERED), фіксована позиція
    /// TopLeft (не перетинається з Anti-AFK у TopRight).
    /// Не залежить від Hangar Overlay та Anti-AFK — незалежний життєвий цикл.
    /// </summary>
    public partial class AutoKeyIndicatorWindow : Window
    {
        private const double EdgeMargin = 16;

        private const int GwlExStyle = -20;
        private const int WsExLayered = 0x80000;
        private const int WsExTransparent = 0x20;

        // Кольори станів.
        private static readonly SolidColorBrush ColorOff = new(Color.FromRgb(0x44, 0x50, 0x5A));    // сірий
        private static readonly SolidColorBrush ColorRunning = new(Color.FromRgb(0x5B, 0xC9, 0x8A)); // зелений
        private static readonly SolidColorBrush ColorPaused = new(Color.FromRgb(0xFF, 0xC1, 0x07));  // жовтий/бурштиновий

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

        public AutoKeyIndicatorWindow()
        {
            InitializeComponent();
            SourceInitialized += OnSourceInitialized;
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            ApplyClickThrough();
            PositionTopLeft();
        }

        /// <summary>
        /// Встановлює WS_EX_TRANSPARENT | WS_EX_LAYERED — кліки проходять крізь вікно.
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

        private void PositionTopLeft()
        {
            Left = EdgeMargin;
            Top = EdgeMargin;
        }

        /// <summary>
        /// Оновлює вигляд індикатора відповідно до стану Auto Key.
        /// </summary>
        public void UpdateState(AutoKeyState state)
        {
            switch (state)
            {
                case AutoKeyState.Running:
                    Dot.Fill = ColorRunning;
                    Label.Text = "Auto Key";
                    break;

                case AutoKeyState.Paused:
                    Dot.Fill = ColorPaused;
                    Label.Text = "Auto Key · Paused";
                    break;

                default: // Off
                    Dot.Fill = ColorOff;
                    Label.Text = "Auto Key";
                    break;
            }
        }
    }
}
