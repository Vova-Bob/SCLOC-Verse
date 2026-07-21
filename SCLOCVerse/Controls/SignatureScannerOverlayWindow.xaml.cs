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
        /// Конвертер hex-рядка («#RRGGBB») → SolidColorBrush.
        ///
        /// Реалізація через <see cref="System.Windows.Media.Color.FromRgb"/> замість
        /// <c>ColorConverter.ConvertFromString</c> — без винятків, без reflection,
        /// швидше і надійніше у WPF-overlay з AllowsTransparency. Усі наші hex мають
        /// формат 7 символів (#RRGGBB), перевірено статично.
        /// </summary>
        private static System.Windows.Media.SolidColorBrush BrushFromHex(string hex)
        {
            if (hex is null || hex.Length != 7 || hex[0] != '#')
                return System.Windows.Media.Brushes.White;

            try
            {
                byte r = Convert.ToByte(hex.Substring(1, 2), 16);
                byte g = Convert.ToByte(hex.Substring(3, 2), 16);
                byte b = Convert.ToByte(hex.Substring(5, 2), 16);
                return new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(r, g, b));
            }
            catch
            {
                return System.Windows.Media.Brushes.White;
            }
        }

        public void UpdateState(MiningState state)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<MiningState>(UpdateState), state);
                return;
            }

            TitleLabel.Text = "SC SCAN";

            var candidates = state?.AllCandidates ?? System.Array.Empty<MiningMaterial>();

            if (candidates.Count == 0)
            {
                // ── Сигнатура не розпізнано ──
                // Розрізняємо два стани:
                //   1. "Сигнал втрачено" — раніше був результат (LastGoodResultUtc != null),
                //      але OCR його більше не бачить і ResultAge timeout минув.
                //   2. "Сканування..." — результату ще не було (початковий стан / Discovery).
                ShowSingleCandidate();

                var wasLost = state?.LastGoodResultUtc is null
                              && !string.IsNullOrEmpty(state?.RawCode)
                              && state?.Confidence > 0;

                MaterialName.Text = wasLost ? "Сигнал втрачено" : (state?.RawCode ?? "Сканування...");
                MaterialName.Foreground = wasLost
                    ? BrushFromHex("#FF6B6B")    // червонуватий для "втрачено"
                    : BrushFromHex("#E8F3FF");   // нейтральний білий

                RarityLabelValue.Text = "—";
                RarityLabelValue.Foreground = BrushFromHex("#A7C6E7");
                StarsLabel.Text = "☆☆☆☆☆";

                ClusterInfo.Text = "—";
                SignatureLabel.Text = "—";

                UpdateScanProgress(0);
                Confidence.Text = state is not null
                    ? $"conf: {state.Confidence:F2}"
                    : "—";
                return;
            }

            if (candidates.Count == 1)
            {
                // ── Однозначний збіг (звичайний material) — single-candidate блок ──
                ShowSingleCandidate();
                RenderSingleCandidate(candidates[0], state!);
            }
            else
            {
                // ── Колізія (ROC/FPS/Salvage) — multi-candidate блок ──
                ShowMultiCandidate();
                RenderMultiCandidate(candidates, state!);
            }

            UpdateScanProgress(state!.Confidence);
            Confidence.Text = $"conf: {state.Confidence:F2}";
        }

        /// <summary>Показати single-candidate блок, приховати multi-candidate.</summary>
        private void ShowSingleCandidate()
        {
            SingleCandidatePanel.Visibility = Visibility.Visible;
            MultiCandidatePanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>Показати multi-candidate блок, приховати single-candidate.</summary>
        private void ShowMultiCandidate()
        {
            SingleCandidatePanel.Visibility = Visibility.Collapsed;
            MultiCandidatePanel.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Рендер single-candidate блоку (назва, рідкість, зірки, кластер, сігнатура).
        /// Використовується як при Count == 1, так і при Count == 0 (з placeholder-значеннями).
        /// </summary>
        private void RenderSingleCandidate(MiningMaterial material, MiningState state)
        {
            var rarity = MiningRarityRegistry.Get(material.Name);

            // 1. Назва (кольорова за рідкістю).
            MaterialName.Text = material.Name;
            MaterialName.Foreground = BrushFromHex(rarity.ColorHex);

            // 2. Текстова рідкість.
            //    «Рідкість:» — білий (фіксований у XAML через RarityLabelPrefix).
            //    Значення (напр. «Epic») — колір рідкісності.
            RarityLabelValue.Text = rarity.DisplayName;
            RarityLabelValue.Foreground = BrushFromHex(rarity.ColorHex);

            // 3. Зірки: текст оновлюємо, колір ЗАВЖДИ золотистий (фіксований у XAML).
            StarsLabel.Text = rarity.Stars;

            // 4. Кластер — обрізаємо «Cluster: » prefix з ClusterFormat.
            //    У DB ClusterFormat створюється з ПІДСТАВЛЕНИМ значенням («Cluster: 2 Rocks»),
            //    а не з шаблоном «{0}». Лейбл «Кластер:» вже у XAML — дублювання прибираємо.
            //    Для ROC/FPS/Salvage («Tier N») — залишаємо як є.
            ClusterInfo.Text = FormatClusterDisplay(material.ClusterFormat);

            // 5. Сигнатура — реальна сігнатура кластера (raw = base × cluster),
            //    НЕ базова сігнатура 1 каменя. Обчислення ТІЛЬКИ для UI-відображення;
            //    бізнес-логіка визначення ресурсу/кластера не зачіпається.
            //    У Discovery mode state.RawCode містить базову сигнатуру (що знайшов
            //    OcrFullScanLocator), тому обчислюємо raw самостійно.
            SignatureLabel.Text = FormatSignatureDisplay(material, state.RawCode);
        }

        /// <summary>
        /// Рендер multi-candidate блоку при колізії (ROC/FPS/Salvage).
        /// Показує сігнатуру + список усіх кандидатів + progress bar.
        /// </summary>
        private void RenderMultiCandidate(IReadOnlyList<MiningMaterial> candidates, MiningState state)
        {
            // Сигнатура як заголовок — обчислюємо з першого кандидата (для ROC/FPS/Salvage
            // base немає, тому використовуємо state.RawCode якщо валідний, інакше material.Code).
            var sig = FormatGenericSignature(candidates[0], state.RawCode);
            MultiSignatureLabel.Text = sig;

            MultiCountLabel.Text = candidates.Count == 2
                ? "2 варіанти (колізія)"
                : $"{candidates.Count} варіантів (колізія)";

            // Прив'язка списку кандидатів (ROC → FPS → Salvage).
            CandidatesList.ItemsSource = candidates;
        }

        /// <summary>
        /// Форматування сігнатури для generic-кандидата (ROC/FPS/Salvage).
        /// Generic не має base signature у DefaultMiningSignatures, тому використовуємо
        /// material.Code (який = raw.ToString() у LookupGenericAll) або state.RawCode.
        /// </summary>
        private static string FormatGenericSignature(MiningMaterial material, string? rawCodeFallback)
        {
            // material.Code = raw.ToString() — встановлюється у LookupGenericAll.
            if (!string.IsNullOrEmpty(material.Code)) return material.Code;
            return rawCodeFallback ?? "—";
        }

        /// <summary>
        /// Форматування відображення кластера без дублювання «Cluster:».
        ///
        /// <para>Кейси:</para>
        /// <list type="bullet">
        /// <item>«Cluster: 2 Rocks» → «2 Rocks» (звичайні матеріали).</item>
        /// <item>«Tier 3»           → «Tier 3» (ROC/FPS/Salvage, без змін).</item>
        /// <item>null               → «—».</item>
        /// </list>
        /// </summary>
        private static string FormatClusterDisplay(string? clusterFormat)
        {
            if (string.IsNullOrEmpty(clusterFormat)) return "—";

            // Звичайні матеріали: «Cluster: N Rocks» → «N Rocks».
            const string clusterPrefix = "Cluster: ";
            if (clusterFormat.StartsWith(clusterPrefix, StringComparison.Ordinal))
                return clusterFormat[clusterPrefix.Length..];

            // ROC/FPS/Salvage: «Tier N» → без змін.
            return clusterFormat;
        }

        /// <summary>
        /// Форматування відображення сигнатури: реальна сигнатура кластера.
        ///
        /// <para>raw = base × cluster, де:</para>
        /// <list type="bullet">
        /// <item><c>base</c> — з <see cref="Services.Mining.Signatures.DefaultMiningSignatures.Materials"/>
        ///     (базова сігнатура 1 каменя для матеріалу).</item>
        /// <item><c>cluster</c> — витягнується з <see cref="MiningMaterial.ClusterFormat"/>
        ///     (DB підставляє реальне значення у «Cluster: N Rocks»).</item>
        /// </list>
        ///
        /// <para>Якщо base або cluster невідомі (ROC/FPS/Salvage, override) —
        /// повертаємо <paramref name="rawCodeFallback"/> (state.RawCode) як є.</para>
        ///
        /// <para><b>Важливо:</b> це ТІЛЬКИ UI-обчислення. Логіка визначення
        /// кластера (у <see cref="Services.Mining.Signatures.MiningSignatureDatabase"/>)
        /// не зачіпається.</para>
        /// </summary>
        private static string FormatSignatureDisplay(MiningMaterial material, string? rawCodeFallback)
        {
            // Витягнути N з «Cluster: N Rocks» (або будь-якого формату з числом).
            var clusterNumber = TryExtractClusterNumber(material.ClusterFormat);
            var baseSig = MiningRarityRegistry.TryGetBaseSignature(material.Name);

            if (clusterNumber.HasValue && baseSig.HasValue && clusterNumber.Value > 0)
            {
                var raw = baseSig.Value * clusterNumber.Value;
                // Формат «16,960» (з роздільником тисяч) — як у HUD Star Citizen.
                return raw.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            }

            // Fallback: показати raw code з OCR, якщо є; інакше material.Code.
            return rawCodeFallback ?? material.Code ?? "—";
        }

        /// <summary>
        /// Витягнути число кластера з ClusterFormat (напр. «Cluster: 2 Rocks» → 2).
        /// </summary>
        private static int? TryExtractClusterNumber(string? clusterFormat)
        {
            if (string.IsNullOrEmpty(clusterFormat)) return null;

            // Перша послідовність цифр у рядку.
            var match = System.Text.RegularExpressions.Regex.Match(clusterFormat, @"\d+");
            return match.Success && int.TryParse(match.Value, out var n) ? n : null;
        }

        /// <summary>
        /// Перетворити confidence (0..1) на 10-сегментний progress bar + відсотки.
        /// 1.0 → «██████████ 100%», 0.85 → «████████░░ 85%».
        ///
        /// <para>Оновлює ОБИДВА progress bar-и (Single + Multi candidate),
        /// бо активний лише один з них (Visibility), а другий просто не видний.</para>
        /// </summary>
        private void UpdateScanProgress(double confidence)
        {
            var pct = (int)Math.Round(Math.Clamp(confidence, 0.0, 1.0) * 100);
            var filled = (int)Math.Round(confidence * 10);
            if (filled < 0) filled = 0;
            if (filled > 10) filled = 10;

            var barText = new string('█', filled) + new string('░', 10 - filled);
            var pctText = $"{pct}%";

            // Підфарбовування бару за рівнем довіри (green ≥0.9, cyan інакше).
            var barColor = confidence >= 0.9 ? "#3DD6A8" : "#5F9FE0";
            var barBrush = BrushFromHex(barColor);

            // Single-candidate bar.
            ScanBar.Text = barText;
            ScanBar.Foreground = barBrush;
            ScanPercent.Text = pctText;

            // Multi-candidate bar.
            MultiScanBar.Text = barText;
            MultiScanBar.Foreground = barBrush;
            MultiScanPercent.Text = pctText;
        }
    }
}