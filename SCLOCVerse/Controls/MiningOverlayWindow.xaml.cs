using SCLOCVerse.Models.Mining;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SCLOCVerse.Controls
{
    /// <summary>
    /// Overlay-вікно для Mining Module: компактний badge з матеріалом + кластером,
    /// click-through (WS_EX_TRANSPARENT | WS_EX_LAYERED), Topmost.
    /// Незалежний життєвий цикл (як AutoKeyIndicatorWindow).
    /// </summary>
    public partial class MiningOverlayWindow : Window
    {
        private const double EdgeMargin = 16;

        private const int GwlExStyle = -20;
        private const int WsExLayered = 0x80000;
        private const int WsExTransparent = 0x20;

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

        public MiningOverlayWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // TopRight позиція за замовчуванням (не перетинається з AutoKey TopLeft).
            var screen = SystemParameters.WorkArea;
            Left = screen.Width - Width - EdgeMargin;
            Top = EdgeMargin;

            EnableClickThrough();
        }

        /// <summary>
        /// Увімкнути click-through: WS_EX_TRANSPARENT | WS_EX_LAYERED.
        /// </summary>
        private void EnableClickThrough()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var extended = GetWindowLong(hwnd, GwlExStyle);
            SetWindowLong(hwnd, GwlExStyle, new IntPtr(extended | WsExLayered | WsExTransparent));
        }

        /// <summary>
        /// Оновити вміст overlay з MiningState.
        /// </summary>
        public void UpdateState(MiningState state)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<MiningState>(UpdateState), state);
                return;
            }

            if (state?.Material is null)
            {
                MaterialName.Text = state?.RawCode ?? "Mining...";
                ClusterInfo.Text = "—";
                Confidence.Text = "confidence: —";
            }
            else
            {
                MaterialName.Text = state.Material.Name;

                var cluster = string.IsNullOrEmpty(state.ClusterCount) ? "?" : state.ClusterCount;
                ClusterInfo.Text = state.Material.ClusterFormat?.Replace("{0}", cluster)
                                   ?? $"Cluster: {cluster}";

                Confidence.Text = $"confidence: {state.Confidence:F2} | code: {state.RawCode ?? "?"}";
            }
        }
    }
}