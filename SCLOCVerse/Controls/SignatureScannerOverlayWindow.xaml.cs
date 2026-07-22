using SCLOCVerse.Models.Mining;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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
        private const double ScanBarTrackWidth = 60;

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

        // Echo blips: рандомізація координат при кожному вході в режим Scanning.
        // Радар Canvas 140×140, центр (70,70). r ∈ [15, 62] — безпечно всередині кільця.
        private readonly Random _blipRng = new();
        private const double BlipCenterX = 70.0;
        private const double BlipCenterY = 70.0;
        private const double BlipRadiusMin = 15.0;
        private const double BlipRadiusMax = 62.0;
        private const double BlipMinDistance = 12.0;
        private const int BlipMaxAttempts = 50;
        private const double BlipHalfSize = 1.5; // Ellipse 3×3 — зміщення для центрування

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

        /// <summary>Тривалість fade transition (мс) — swap між Scanning та Result.</summary>
        private const int FadeMs = 200;

        public void UpdateState(MiningState state)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<MiningState>(UpdateState), state);
                return;
            }

            TitleLabel.Text = "SC SCAN";

            var candidates = state?.AllCandidates ?? System.Array.Empty<MiningMaterial>();
            var scanState = state?.ScanState ?? MiningScanState.Idle;

            if (candidates.Count == 0)
            {
                // ── Сигнатура не розпізнано ──
                // ScanState визначає відображення:
                //   Lost → "Сигнал втрачено" (#FF6B6B) у single-candidate блоку.
                //   Scanning/Idle → Holographic Radar (Блок S, "Пошук сигнатур...").
                var isLost = scanState == MiningScanState.Lost;

                if (isLost)
                {
                    // Сигнал втрачено — показати в single-candidate блоці (не радар).
                    ShowSingleCandidate();

                    MaterialName.Text = "Сигнал втрачено";
                    MaterialName.Foreground = BrushFromHex("#FF6B6B");
                    RarityLabelValue.Text = "—";
                    RarityLabelValue.Foreground = BrushFromHex("#A7C6E7");
                    StarsLabel.Text = "☆☆☆☆☆";
                    ClusterInfo.Text = "—";
                    SignatureLabel.Text = "—";
                    UpdateScanProgress(0);
                }
                else
                {
                    // Пошук сигнатур — показати радар.
                    ShowScanningRadar();
                }

                Confidence.Text = state is not null
                    ? $"conf: {state.Confidence:F2}"
                    : "—";
                return;
            }

            // ── Сигнатура знайдена — приховати радар, показати картку ──
            if (candidates.Count == 1)
            {
                ShowSingleCandidate();
                RenderSingleCandidate(candidates[0], state!);
            }
            else
            {
                ShowMultiCandidate();
                RenderMultiCandidate(candidates, state!);
            }

            UpdateScanProgress(state!.Confidence);
            Confidence.Text = $"conf: {state.Confidence:F2}";
        }

        /// <summary>
        /// Показати Scanning Radar (Блок S), приховати результати.
        /// Fade-out результат, fade-in радар (200мс).
        /// </summary>
        private void ShowScanningRadar()
        {
            if (ScanningPanel.Visibility == Visibility.Visible
                && SingleCandidatePanel.Visibility == Visibility.Collapsed
                && MultiCandidatePanel.Visibility == Visibility.Collapsed)
                return; // вже активний — не рандомізуємо, щоб точки не стрибали

            ScanningPanel.Visibility = Visibility.Visible;
            ScanningPanel.Opacity = 0;
            SingleCandidatePanel.Visibility = Visibility.Collapsed;
            MultiCandidatePanel.Visibility = Visibility.Collapsed;

            // Нові випадкові координати для echo blips при кожному вході в Scanning.
            RandomizeBlipPositions();

            // Fade-in радар.
            var fade = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0, To = 1,
                Duration = TimeSpan.FromMilliseconds(FadeMs)
            };
            ScanningPanel.BeginAnimation(OpacityProperty, fade);
        }

        /// <summary>
        /// Згенерувати нові випадкові координати для Blip1..Blip4.
        /// Обмеження: r ∈ [BlipRadiusMin, BlipRadiusMax] від центру (70,70);
        /// мінімальна відстань між точками ≥ BlipMinDistance.
        /// Жодних змін Storyboard / BeginTime / Scale / Opacity-curve.
        /// </summary>
        private void RandomizeBlipPositions()
        {
            var blips = new[] { Blip1, Blip2, Blip3, Blip4 };
            var placed = new (double X, double Y)[blips.Length];

            for (int i = 0; i < blips.Length; i++)
            {
                double cx = 0, cy = 0;
                int attempts = 0;
                do
                {
                    var r = BlipRadiusMin + _blipRng.NextDouble() * (BlipRadiusMax - BlipRadiusMin);
                    var a = _blipRng.NextDouble() * Math.PI * 2.0;
                    cx = BlipCenterX + r * Math.Cos(a);
                    cy = BlipCenterY + r * Math.Sin(a);
                    attempts++;
                }
                while (attempts < BlipMaxAttempts && BlipTooClose(cx, cy, placed, i));

                placed[i] = (cx, cy);
                Canvas.SetLeft(blips[i], cx - BlipHalfSize);
                Canvas.SetTop(blips[i], cy - BlipHalfSize);
            }
        }

        /// <summary>
        /// Перевірка мінімальної відстані від (x,y) до вже розміщених точок.
        /// </summary>
        private static bool BlipTooClose(double x, double y, (double X, double Y)[] placed, int count)
        {
            var minDistSq = BlipMinDistance * BlipMinDistance;
            for (int i = 0; i < count; i++)
            {
                var dx = x - placed[i].X;
                var dy = y - placed[i].Y;
                if (dx * dx + dy * dy < minDistSq)
                    return true;
            }
            return false;
        }


        /// <summary>Показати single-candidate блок, приховати радар та multi-candidate.</summary>
        private void ShowSingleCandidate()
        {
            var wasScanning = ScanningPanel.Visibility == Visibility.Visible;

            ScanningPanel.Visibility = Visibility.Collapsed;
            MultiCandidatePanel.Visibility = Visibility.Collapsed;
            SingleCandidatePanel.Visibility = Visibility.Visible;

            if (wasScanning)
            {
                SingleCandidatePanel.Opacity = 0;
                var fade = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0, To = 1,
                    Duration = TimeSpan.FromMilliseconds(FadeMs)
                };
                SingleCandidatePanel.BeginAnimation(OpacityProperty, fade);
            }
        }

        /// <summary>Показати multi-candidate блок, приховати радар та single-candidate.</summary>
        private void ShowMultiCandidate()
        {
            var wasScanning = ScanningPanel.Visibility == Visibility.Visible;

            ScanningPanel.Visibility = Visibility.Collapsed;
            SingleCandidatePanel.Visibility = Visibility.Collapsed;
            MultiCandidatePanel.Visibility = Visibility.Visible;

            if (wasScanning)
            {
                MultiCandidatePanel.Opacity = 0;
                var fade = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0, To = 1,
                    Duration = TimeSpan.FromMilliseconds(FadeMs)
                };
                MultiCandidatePanel.BeginAnimation(OpacityProperty, fade);
            }
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
        /// Оновити графічний progress bar + відсотки на основі confidence (0..1).
        /// Графічний бар замість текстового — усуває артефакти рендерингу
        /// Unicode-блоків (█░) на Layered Window (AllowsTransparency).
        ///
        /// <para>Оновлює ОБИДВА progress bar-и (Single + Multi candidate),
        /// бо активний лише один з них (Visibility), а другий просто не видний.</para>
        /// </summary>
        private void UpdateScanProgress(double confidence)
        {
            var pct = (int)Math.Round(Math.Clamp(confidence, 0.0, 1.0) * 100);
            var pctText = $"{pct}%";

            var clamped = Math.Clamp(confidence, 0.0, 1.0);
            var fillWidth = clamped * ScanBarTrackWidth;

            var barColor = confidence >= 0.9 ? "#3DD6A8" : "#5F9FE0";
            var barBrush = BrushFromHex(barColor);

            // Single-candidate bar.
            ScanBarFill.Width = fillWidth;
            ScanBarFill.Fill = barBrush;
            ScanPercent.Text = pctText;

            // Multi-candidate bar.
            MultiScanBarFill.Width = fillWidth;
            MultiScanBarFill.Fill = barBrush;
            MultiScanPercent.Text = pctText;
        }
    }
}