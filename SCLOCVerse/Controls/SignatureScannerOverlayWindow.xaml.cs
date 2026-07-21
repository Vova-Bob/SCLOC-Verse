using SCLOCVerse.Models.Mining;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace SCLOCVerse.Controls
{
    using System.Diagnostics;

    /// <summary>
    /// Overlay "SC SCAN" — результат розпізнавання сигнатури.
    ///
    /// Патерн: як HangarOverlayWindow.
    /// - Click-through за замовчуванням (WS_EX_TRANSPARENT).
    /// - Temporary Drag Mode: hotkey (Ctrl+Alt+\) вимикає click-through,
    ///   користувач перетягує ЛКМ, відпускає — click-through вмикається знову.
    /// - Прозорість: hotkeys (Ctrl+Alt+[ / Ctrl+Alt+]).
    /// - Foreground gate: показується лише коли Star Citizen активний.
    /// - Position/Opacity зберігаються у Settings.
    /// </summary>
    public partial class SignatureScannerOverlayWindow : Window
    {
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

        private bool _allowClose;
        private bool _clickThrough = true;
        private bool _clickThroughTemp;
        private Point? _dragStart;

        public double SavedOpacity { get; set; } = 0.9;
        public event EventHandler<Rect>? PositionChanged;

        // Opacity steps (як Hangar: 0.05 крок).
        private const double OpacityStep = 0.1;
        private const double OpacityMin = 0.2;
        private const double OpacityMax = 1.0;

        public SignatureScannerOverlayWindow()
        {
            InitializeComponent();
            SourceInitialized += OnSourceInitialized;
            Closing += OnClosing;

            MouseLeftButtonDown += OnMouseLeftButtonDown;
            MouseMove += OnMouseMove;
            MouseLeftButtonUp += OnMouseLeftButtonUp;
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var screen = SystemParameters.WorkArea;
            if (Left == 0 && Top == 0)
            {
                Left = screen.Width - Width - 16;
                Top = 16;
            }

            Opacity = SavedOpacity;
            ApplyClickThrough(true);
        }

        /// <summary>
        /// Foreground gate — показувати overlay лише коли Star Citizen активний.
        /// </summary>
        public void UpdateVisibility(bool isStarCitizenForeground)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<bool>(UpdateVisibility), isStarCitizenForeground);
                return;
            }

            if (isStarCitizenForeground)
            {
                if (!IsVisible) Show();
            }
            else
            {
                if (IsVisible) Hide();
            }
        }

        // ── Temporary Drag Mode (як HangarOverlayWindow) ──

        public void BeginTemporaryDragMode()
        {
            if (_clickThrough && !_clickThroughTemp)
            {
                _clickThroughTemp = true;
                ApplyClickThrough(false);
                Debug.WriteLine("[SCScan] Drag mode ON (click-through disabled)");
            }
        }

        public void EndTemporaryDragMode()
        {
            if (_clickThroughTemp)
            {
                _clickThroughTemp = false;
                ApplyClickThrough(true);
                Debug.WriteLine("[SCScan] Drag mode OFF (click-through restored)");
            }
        }

        // ── Opacity через hotkeys ──

        public void IncreaseOpacity()
        {
            Opacity = Math.Clamp(Opacity + OpacityStep, OpacityMin, OpacityMax);
            SavedOpacity = Opacity;
            RaisePositionChanged();
        }

        public void DecreaseOpacity()
        {
            Opacity = Math.Clamp(Opacity - OpacityStep, OpacityMin, OpacityMax);
            SavedOpacity = Opacity;
            RaisePositionChanged();
        }

        // ── Mouse handlers (працюють лише коли click-through вимкнено) ──

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
            if (_dragStart is not null)
            {
                _dragStart = null;
                ReleaseMouseCapture();
                EndTemporaryDragMode();
                RaisePositionChanged();
            }
        }

        // ── Position Changed ──

        private void RaisePositionChanged()
        {
            PositionChanged?.Invoke(this, new Rect(Left, Top, Width, Height));
        }

        // ── Click-through ──

        private void ApplyClickThrough(bool enabled)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            var exStyle = GetWindowLong(hwnd, GwlExStyle);
            if (enabled)
                exStyle |= (WsExTransparent | WsExLayered);
            else
                exStyle = (exStyle | WsExLayered) & ~WsExTransparent;

            SetWindowLong(hwnd, GwlExStyle, exStyle);
        }

        // ── Close protection ──

        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                Debug.WriteLine("[SCScan] Closing cancelled (Alt+F4 blocked).");
            }
        }

        public void AllowClose() => _allowClose = true;

        // ── State update ──
        //
        // Лише ВІЗУАЛІЗАЦІЯ. Бізнес-логіка (визначення ресурсу, кластера, confidence)
        // залишається в MiningRecognitionService / MiningSignatureDatabase без змін.
        // Overlay лише мапить MiningState на нову ієрархічну структуру з рідкістю
        // (MiningRarityRegistry — статичний довідник, не впливає на розпізнавання).

        /// <summary>
        /// Конвертер hex-рядка ("#RRGGBB") → SolidColorBrush.Кешувати не потрібно:
        /// confidence/rarity змінюються рідко (раз на нове розпізнавання).
        /// </summary>
        private static System.Windows.Media.SolidColorBrush BrushFromHex(string hex)
            => new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));

        public void UpdateState(MiningState state)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<MiningState>(UpdateState), state);
                return;
            }

            if (state?.Material is null)
            {
                // ── Сканування ще не завершено (або матеріал не розпізнано) ──
                TitleLabel.Text = "SC SCAN";
                MaterialName.Text = state?.RawCode ?? "Сканування...";
                MaterialName.Foreground = BrushFromHex("#E8F3FF");

                RarityLabel.Text = "Рідкість: —";
                StarsLabel.Text = "☆☆☆☆☆";
                StarsLabel.Foreground = BrushFromHex("#5F7E8A");

                ClusterInfo.Text = "—";
                SignatureLabel.Text = "—";

                // Confidence < 1 → частковий прогрес (показуємо статус сканування).
                UpdateScanProgress(state?.Confidence ?? 0);
                Confidence.Text = state is not null
                    ? $"conf: {state.Confidence:F2}"
                    : "—";
                return;
            }

            // ── Матеріал розпізнано ──
            var material = state.Material;
            var rarity = MiningRarityRegistry.Get(material.Name);

            TitleLabel.Text = "SC SCAN";

            // 1. Назва (кольорова за рідкістю).
            MaterialName.Text = material.Name;
            MaterialName.Foreground = BrushFromHex(rarity.ColorHex);

            // 2. Текстова рідкість.
            RarityLabel.Text = $"Рідкість: {rarity.DisplayName}";
            RarityLabel.Foreground = BrushFromHex(rarity.ColorHex);

            // 3. Зірки.
            StarsLabel.Text = rarity.Stars;
            StarsLabel.Foreground = BrushFromHex(rarity.ColorHex);

            // 4. Кластер (логіка визначення не змінюється — лише візуалізація).
            var cluster = string.IsNullOrEmpty(state.ClusterCount) ? "?" : state.ClusterCount;
            ClusterInfo.Text = material.ClusterFormat?.Replace("{0}", cluster)
                               ?? $"Cluster: {cluster}";

            // 5. Сигнатура (raw code з OCR).
            SignatureLabel.Text = state.RawCode ?? "—";

            // 6. Confidence → progress bar.
            UpdateScanProgress(state.Confidence);
            Confidence.Text = $"conf: {state.Confidence:F2}";
        }

        /// <summary>
        /// Перетворити confidence (0..1) на 10-сегментний progress bar + відсотки.
        /// 1.0 → «██████████ 100%», 0.85 → «████████░░ 85%».
        /// </summary>
        private void UpdateScanProgress(double confidence)
        {
            var pct = (int)Math.Round(Math.Clamp(confidence, 0.0, 1.0) * 100);
            var filled = (int)Math.Round(confidence * 10);
            if (filled < 0) filled = 0;
            if (filled > 10) filled = 10;

            ScanBar.Text = new string('█', filled) + new string('░', 10 - filled);
            ScanPercent.Text = $"{pct}%";

            // Підфарбовування бару за рівнем довіри (green ≥0.9, cyan інакше).
            var barColor = confidence >= 0.9 ? "#3DD6A8" : "#5F9FE0";
            ScanBar.Foreground = BrushFromHex(barColor);
        }
    }
}