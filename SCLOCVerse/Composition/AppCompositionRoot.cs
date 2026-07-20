using SCLOCVerse.Helpers;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Observability;
using SCLOCVerse.Services;
using SCLOCVerse.Services.AntiAfk;
using SCLOCVerse.Services.AutoKey;
using SCLOCVerse.Services.ApplicationInstance;
using SCLOCVerse.Services.ApplicationUpdate;
using SCLOCVerse.Services.Autostart;
using SCLOCVerse.Services.Common;
using SCLOCVerse.Services.HangarTimer;
using SCLOCVerse.Services.InputSystem;
using SCLOCVerse.Services.LiaServices;
using SCLOCVerse.Services.LocalizationServices;
using SCLOCVerse.Services.Mining;
using SCLOCVerse.Services.Mining.Locators;
using SCLOCVerse.Services.Mining.Overlay;
using SCLOCVerse.Services.Mining.Signatures;
using SCLOCVerse.Services.Notifications;
using SCLOCVerse.Services.Observability;
using SCLOCVerse.Services.OcrPlatform.Captures;
using SCLOCVerse.Services.OcrPlatform.Engines;
using SCLOCVerse.Services.OcrPlatform.Coordinator;
using SCLOCVerse.Services.OcrPlatform.Pipeline;
using SCLOCVerse.Services.OcrPlatform.Validation;
using SCLOCVerse.Services.Tray;
using SCLOCVerse.ViewModels;
using System.Net.Http;
using System.Windows.Threading;


namespace SCLOCVerse.Composition
{
    public partial class AppCompositionRoot : IDisposable
    {
        private readonly IIgnoreRulesProvider _ignoreRulesProvider;
        private readonly IFolderSearchService _folderSearchService;
        private readonly ISettingsService _settingsService;
        private readonly IPreferencesService _preferencesService;
        private readonly IUpdater _updater;
        private readonly UpdateCheckerService _updateCheckerService;
        private readonly IApplicationVersionProvider _applicationVersionProvider;
        private readonly IUpdateChannelService _updateChannelService;
        private readonly IApplicationUpdateService _applicationUpdateService;
        private readonly ILocalizationInstaller _localizationInstaller;
        private readonly IBackgroundUpdateMonitor _backgroundUpdateMonitor;
        private readonly INotificationRouter _notificationRouter;
        private readonly IUpdateDownloader _updateDownloader;
        private readonly IUpdateInstaller _updateInstaller;
        private readonly IUpdateHistoryService _updateHistoryService;
        private readonly IUpdateVerifier _updateVerifier;
        private readonly IGitHubReleaseClient _gitHubReleaseClient;
        private readonly IDialogService _dialogService;
        private readonly AuthCompositionRoot _authCompositionRoot;
        private readonly TelemetryClient _telemetryClient;

        private readonly IHangarSettingsService _hangarSettingsService;
        private readonly IHangarStartTimeProvider _hangarStartTimeProvider;
        private readonly IHangarOverlayService _hangarOverlayService;
        private readonly IHotkeyBackend _hotkeyBackend;
        private readonly IHotkeyService _hotkeyService;
        private readonly IHangarTimerService _hangarTimerService;
        private readonly ITrayService _trayService;
        private readonly IApplicationInstanceService _applicationInstanceService;
        private readonly IAutostartService _autostartService;
        private readonly IToastNotificationService _toastNotificationService;
        private readonly IAntiAfkService _antiAfkService;
        private readonly IAutoKeyService _autoKeyService;

        // OCR Platform — Services/OcrPlatform/* (Epic 2-6 завершено).
        private readonly IScreenCaptureService _screenCaptureService;
        private readonly IOcrEngine _ocrEngine;

        // OCR Platform — Coordinator + Validation + Region Registry.
        private readonly IOcrRegionRegistry _ocrRegionRegistry;
        private readonly IImagePipeline _imagePipeline;
        private readonly IResultValidator _resultValidator;
        private readonly IOcrCoordinator _ocrCoordinator;

        // Mining Module (Epic 7) — перший consumer OCR Platform.
        private readonly IMiningSignatureDatabase _miningSignatures;
        private readonly IMiningRoiResolver _miningRoiResolver;
        private readonly IMiningHudLocatorStrategy _miningHudLocator;
        private readonly IMiningRecognitionService _miningRecognition;
        private readonly IMiningOverlayService _miningOverlay;

        public AppCompositionRoot()
        {
            // Single Instance + IPC: створюється найпершим, бо визначає,
            // чи цей процес — перший (IsFirstInstance), від чого залежить
            // подальший сценарій (старт UI vs IPC-сигнал + вихід).
            _applicationInstanceService = new ApplicationInstanceService();

            _ignoreRulesProvider = new IgnoreRulesProvider();
            _folderSearchService = new FolderSearchService(_ignoreRulesProvider);
            _settingsService = new SettingsService();
            // SettingsService реалізує ISettingsService + IUpdateChannelService + IPreferencesService
            // через один об'єкт (Варіант A) — єдиний клас над %LocalAppData%\SCLOCVerse\user.config.
            _preferencesService = (IPreferencesService)_settingsService;

            // SCLOC Observability Platform — конструюється найраніше (без Supabase-клієнта),
            // щоб інжектитись у всі сервіси оновлення та auth, включно з L.I.A.-Updater.
            // Дешеве (Конституція, Стаття 3). Будь-яке створення сервісів, що залежать від
            // телеметрії, має відбуватись ПІСЛЯ цього блоку, інакше вони отримають null.
            var telemetryChannel = string.IsNullOrWhiteSpace(SCLOCVerse.Settings.Default.UpdateChannel)
                ? "stable"
                : SCLOCVerse.Settings.Default.UpdateChannel;
            _telemetryClient = new TelemetryClient(BuildInfo.Create(telemetryChannel), enabled: !IsTelemetryDisabled());

            _updater = new Updater(_telemetryClient);
            _updateCheckerService = new UpdateCheckerService(_updater);

            _applicationVersionProvider = new ApplicationVersionProvider();
            _updateChannelService = (IUpdateChannelService)_settingsService;

            var httpClient = new HttpClient();
            var gitHubClient = new GitHubReleaseClient(httpClient, HttpRetryHelper.UserAgent);
            var updateCacheService = new UpdateCacheService();

            _hangarSettingsService = new HangarSettingsService();
            _hangarStartTimeProvider = new HangarStartTimeProvider(httpClient, _hangarSettingsService);

            // HotkeyService створюється ДО HangarOverlayService: overlay використовує
            // GetDefinitions() для динамічної підказки гарячих клавіш (SSOT, без дублювання).
            _hotkeyBackend = CreateHotkeyBackend();
            var diagnosticsEnabled = IsHotkeyDiagnosticsEnabled();
            _hotkeyService = new HotkeyService(_hotkeyBackend, diagnosticsEnabled, new HotkeyBindingsStore());

            _hangarOverlayService = new HangarOverlayService(_hangarSettingsService, _hotkeyService);
            _hangarTimerService = new HangarTimerService(
                _hangarStartTimeProvider,
                _hangarOverlayService,
                _hangarSettingsService,
                _hotkeyService);

            // Anti-AFK: 2 залежності (хоткеї + налаштування). Повністю незалежний від Hangar Overlay.
            _antiAfkService = new AntiAfkService(_hotkeyService, _preferencesService);

            // Auto Key: 2 залежності (хоткеї + налаштування). Stateless foreground-гейт,
            // повністю незалежний від Anti-AFK та Hangar Timer.
            _autoKeyService = new AutoKeyService(_hotkeyService, _preferencesService);

            // OCR Platform — Screen Capture (Epic 2). GDI CopyFromScreen — default,
            // працює з borderless fullscreen (типовий режим Star Citizen).
            // WindowsGraphicsCaptureService (exclusive fullscreen) — future enhancement.
            _screenCaptureService = new GdiScreenCaptureService();

            // OCR Platform — PaddleOCR Engine (Epic 4).
            // PP-OCRv6_small ONNX моделі через Direct ONNX Runtime.
            // v6: покращене розпізнавання цифрових дисплеїв, 50 мов, швидший inference.
            var modelsDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "OcrModels");
            _ocrEngine = new PaddleOcrEngine(
                detModelPath: System.IO.Path.Combine(modelsDir, "ch_PP-OCRv6_det_small.onnx"),
                recModelPath: System.IO.Path.Combine(modelsDir, "ch_PP-OCRv6_rec_small.onnx"),
                dictPath: System.IO.Path.Combine(modelsDir, "ppocrv6_dict.txt"));

            // OCR Platform — Pipeline + Validation + Coordinator (Epic 3-6).
            _imagePipeline = new DefaultImagePipeline();
            _resultValidator = new ResultValidator();
            _ocrRegionRegistry = new OcrRegionRegistry();
            _ocrCoordinator = new OcrCoordinator(
                _ocrRegionRegistry,
                _screenCaptureService,
                _imagePipeline,
                _ocrEngine,
                _resultValidator);

            // Mining Module (Epic 7) — перший consumer OCR Platform.
            _miningSignatures = new MiningSignatureDatabase();
            _miningRoiResolver = new MiningRoiResolver();
            // HUD Locator — робоча стратегія: повноекранний OCR + DB lookup.
            // Не залежить від кольору/resolution/DPI — лише від змісту (сигнатури матеріалів).
            // Discovery: повноекранний OCR → знайти відому сигнатуру → повернути bounds.
            // Після локалізації → Tracking (ROI) → швидкий OCR всередині HUD.
            _miningHudLocator = new OcrFullScanLocator(_ocrEngine, _miningSignatures);
            _miningOverlay = new MiningOverlayService(_preferencesService);
            _miningRecognition = new MiningRecognitionService(
                _miningSignatures,
                _ocrCoordinator,
                _ocrRegionRegistry,
                _miningRoiResolver,
                _miningHudLocator,
                _screenCaptureService,
                _miningOverlay,
                _ocrEngine);

            // Wiring: MiningRecognition.StateChanged → MiningOverlay.UpdateState.
            _miningRecognition.StateChanged += (sender, state) => _miningOverlay.UpdateState(state);

            // Wiring: Mining hotkey toggle (Ctrl+Shift+M за замовчуванням).
            // Вмикає/вимикає MiningRecognitionService + Overlay.
            _hotkeyService.Register(new HotkeyDefinition
            {
                Id = HotkeyIds.MiningToggle,
                DefaultGesture = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Shift, HotkeyKey.F10),
                Description = "Увімкнути/Вимкнути Mining Module",
                Handler = _ =>
                {
                    if (_miningRecognition.IsEnabled)
                    {
                        _miningRecognition.Disable();
                        _miningOverlay.Hide();
                    }
                    else
                    {
                        _miningRecognition.Enable();
                        _miningOverlay.Show();
                    }
                    return ValueTask.CompletedTask;
                }
            });

            // Mining overlay: Temporary Drag Mode (Ctrl+Alt+\).
            // Патерн як Hangar Overlay: hotkey → click-through OFF → ЛКМ drag → відпусти → click-through ON.
            _hotkeyService.Register(new HotkeyDefinition
            {
                Id = HotkeyIds.MiningBeginDrag,
                DefaultGesture = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt, HotkeyKey.Oem5),
                Description = "Перетягнути SC Scan overlay",
                Handler = _ =>
                {
                    _miningOverlay.BeginDrag();
                    return ValueTask.CompletedTask;
                }
            });

            // Mining overlay: прозорість вниз (Ctrl+Alt+[).
            _hotkeyService.Register(new HotkeyDefinition
            {
                Id = HotkeyIds.MiningOpacityDown,
                DefaultGesture = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt, HotkeyKey.Oem4),
                Description = "SC Scan: менш прозорий",
                Handler = _ =>
                {
                    _miningOverlay.DecreaseOpacity();
                    return ValueTask.CompletedTask;
                }
            });

            // Mining overlay: прозорість вверх (Ctrl+Alt+]).
            _hotkeyService.Register(new HotkeyDefinition
            {
                Id = HotkeyIds.MiningOpacityUp,
                DefaultGesture = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt, HotkeyKey.Oem6),
                Description = "SC Scan: більш прозорий",
                Handler = _ =>
                {
                    _miningOverlay.IncreaseOpacity();
                    return ValueTask.CompletedTask;
                }
            });

            // TODO T9.1: Calibration UI для ручного вибору регіону (SetManualBounds).

            _applicationUpdateService = new ApplicationUpdateService(
                "Vova-Bob",
                "SCLOC-Verse",
                _applicationVersionProvider,
                _updateChannelService,
                gitHubClient,
                updateCacheService);

            _localizationInstaller = new LocalizationInstaller();

            _backgroundUpdateMonitor = new BackgroundUpdateMonitor(
                _applicationUpdateService,
                _localizationInstaller,
                _updater,
                _settingsService,
                _preferencesService,
                _telemetryClient);

            // NotificationRouter — чистий сервіс (не знає WPF). З'єднується з оркестратором
            // підпискою: UpdateCycleCompleted → Route → NotificationsReady (підписник MainWindow).
            _notificationRouter = new NotificationRouter(_preferencesService);
            _backgroundUpdateMonitor.UpdateCycleCompleted += (s, e) => _notificationRouter.Route(e);

            // Tray-сервіс інкапсулює H.NotifyIcon.TaskbarIcon. Ініціалізується
            // пізніше (Mainlop), коли UI-диспетчер вже готовий.
            _trayService = new TrayService();

            // Autostart-сервіс керує HKCU Run-ключем. Без стану (не тримає
            // дескрипторів), dispose не потрібен. UI-чекбокс буде підключений
            // на Етапі D (Settings).
            _autostartService = new AutostartService();

            // OS Toast-сервіс для Windows Notification Center. Окремий від in-app
            // IToastService. Без стану — dispose не потрібен. Підключається до
            // оркестратора на Етапах E/F.
            _toastNotificationService = new ToastNotificationService();

            _updateDownloader = new UpdateDownloader(httpClient, _telemetryClient);
            _updateInstaller = new UpdateInstaller(new UpdateScriptBuilder(), _telemetryClient);
            _updateHistoryService = new UpdateHistoryService();
            _updateVerifier = new UpdateVerifier(_telemetryClient);
            _gitHubReleaseClient = gitHubClient;
            _dialogService = new DialogService(Dispatcher.CurrentDispatcher);

            var supabaseUrl = GetSupabaseUrl();
            var supabaseAnonKey = GetSupabaseAnonKey();

            _authCompositionRoot = new AuthCompositionRoot(supabaseUrl, supabaseAnonKey, _applicationInstanceService, _telemetryClient);

            // Після побудови auth — підключаємо client + install_id, запускаємо відправку.
            _telemetryClient.SetInstallId(_authCompositionRoot.InstallId);
            _telemetryClient.AttachClientFactory(_authCompositionRoot.ClientFactory);
            // Phase 3.5 Telemetry Policy: gate для Diagnostic-рівня (KB §5.8, §14.19 #118).
            // Єдиний споживач AdvancedDiagnostics (KB §14.19 #122).
            _telemetryClient.AttachDiagnosticGate(() => _preferencesService.GetAdvancedDiagnostics());
        }

        public void Dispose()
        {
            // Спочатку зупиняємо телеметрію: її uploader використовує auth-клієнт,
            // тож глушимо до dispose auth-композиції (reverse-order).
            try { _telemetryClient?.Dispose(); } catch { /* ignore */ }

            // OCR Platform — dispose перед UI/auth залежностями (ONNX sessions важкі).
            if (_ocrEngine is IDisposable ocrDisposable)
            {
                try { ocrDisposable.Dispose(); } catch { /* ignore */ }
            }

            // OCR Coordinator — stop timer + dispose (після Mining disable).
            try { _miningRecognition?.Disable(); } catch { /* ignore */ }
            if (_miningOverlay is IDisposable miningOverlayDisposable)
            {
                try { miningOverlayDisposable.Dispose(); } catch { /* ignore */ }
            }
            try { _ocrCoordinator?.Dispose(); } catch { /* ignore */ }

            // Pipe-сервер єдиного екземпляра зупиняємо раніше за UI-ресурси:
            // інакше другий процес може підключитись у момент, коли UI вже
            // диспознуто, і отримати некоректну відповідь. IAsyncDisposable →
            // блокуємо через GetAwaiter().GetResult() у sync-Dispose.
            try
            {
                if (_applicationInstanceService is IAsyncDisposable asyncDisposable)
                    asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch { /* ignore */ }

            // Tray-іконку прибираємо раніше за інших UI-ресурсів, щоб під час
            // завершення не залишалася фантомна іконка в системному треї.
            try { _trayService?.Dispose(); } catch { /* ignore */ }

            // Спочатку зупиняємо фоновий монітор, щоб його DispatcherTimer
            // не утримував Dispatcher і MainWindow живим.
            if (_backgroundUpdateMonitor is IDisposable backgroundMonitor)
                backgroundMonitor.Dispose();

            // Закриваємо overlay перед hotkey і WpfMessageSource, щоб вікно не
            // залишалося на екрані, якщо процес продовжує завершення.
            if (_hangarOverlayService is IDisposable overlayDisposable)
                overlayDisposable.Dispose();

            // Anti-AFK: зупинка таймера + закриття індикатора (до hangar timer dispose).
            if (_antiAfkService is IDisposable antiAfkDisposable)
                antiAfkDisposable.Dispose();

            // Auto Key: зупинка таймера + закриття індикатора.
            if (_autoKeyService is IDisposable autoKeyDisposable)
                autoKeyDisposable.Dispose();

            if (_hangarTimerService is IDisposable hangarDisposable)
                hangarDisposable.Dispose();

            _authCompositionRoot?.Dispose();
        }

        public AuthCompositionRoot AuthCompositionRoot => _authCompositionRoot;

        /// <summary>Єдина точка спостережуваності (Конституція, Стаття 7/12).</summary>
        public ITelemetryService Telemetry => _telemetryClient;

        /// <summary>Сервіс єдиного екземпляра + IPC активації.</summary>
        public IApplicationInstanceService ApplicationInstance => _applicationInstanceService;

        /// <summary>Сервіс автозапуску з Windows (HKCU Run-ключ).</summary>
        public IAutostartService Autostart => _autostartService;

        /// <summary>Сервіс OS-сповіщень (Notification Center).</summary>
        public IToastNotificationService ToastNotifications => _toastNotificationService;

        /// <summary>Налаштування-переваги користувача (MinimizeToTray, AutoUpdate, Toast Dedup).</summary>
        public IPreferencesService Preferences => _preferencesService;

        public IHangarTimerService HangarTimerService => _hangarTimerService;
        public IHotkeyService HotkeyService => _hotkeyService;
        public IAntiAfkService AntiAfkService => _antiAfkService;
        public IAutoKeyService AutoKeyService => _autoKeyService;

        /// <summary>Сервіс захоплення екрана для OCR Platform.</summary>
        public IScreenCaptureService ScreenCapture => _screenCaptureService;

        /// <summary>OCR Engine (PaddleOCR PP-OCRv5 via ONNX Runtime) для OCR Platform.</summary>
        public IOcrEngine OcrEngine => _ocrEngine;

        /// <summary>Координатор OCR циклу (timer-driven, foreground-gated).</summary>
        public IOcrCoordinator OcrCoordinator => _ocrCoordinator;

        /// <summary>Реєстр регіонів екрана для OCR.</summary>
        public IOcrRegionRegistry OcrRegionRegistry => _ocrRegionRegistry;

        /// <summary>Mining recognition service (перший consumer OCR Platform).</summary>
        public IMiningRecognitionService MiningRecognition => _miningRecognition;

        /// <summary>Mining overlay service (UI badge).</summary>
        public IMiningOverlayService MiningOverlay => _miningOverlay;

        /// <summary>Tray-сервіс для зовнішнього використання (наприклад, App_OnExit).</summary>
        public ITrayService TrayService => _trayService;

        public MainWindow CreateMainWindow(IUiInteractionPolicy uiPolicy)
        {
            var searchFolder = new SearchFolder(_folderSearchService, _settingsService);
            var viewModel = new MainWindowViewModel(searchFolder, _settingsService);

            var windowHelper = new WindowHelper();
            var readmeService = new ReadmeService();

            return new MainWindow(
                viewModel,
                windowHelper,
                _localizationInstaller,
                readmeService,
                _updater,
                _updateCheckerService,
                _applicationUpdateService,
                _backgroundUpdateMonitor,
                _updateChannelService,
                _applicationVersionProvider,
                _updateDownloader,
                _updateInstaller,
                _updateHistoryService,
                _updateVerifier,
                _gitHubReleaseClient,
                _dialogService,
                _authCompositionRoot.AuthService,
                _authCompositionRoot.AuthStatusProvider,
                _hangarTimerService,
                _hotkeyService,
                _trayService,
                _applicationInstanceService,
                _autostartService,
                uiPolicy,
                _preferencesService,
                _notificationRouter,
                _toastNotificationService,
                _antiAfkService,
                _autoKeyService,
                _miningRecognition,
                _miningOverlay,
                _ocrEngine,
                _screenCaptureService);
        }

        private static string GetSupabaseUrl()
        {
            var value = System.Environment.GetEnvironmentVariable("SCLOCVERSE_SUPABASE_URL");
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            value = SCLOCVerse.Properties.SupabaseConfig.DefaultUrl;
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            return "https://placeholder.supabase.co";
        }

        private static string GetSupabaseAnonKey()
        {
            var value = System.Environment.GetEnvironmentVariable("SCLOCVERSE_SUPABASE_ANON_KEY");
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            value = SCLOCVerse.Properties.SupabaseConfig.DefaultAnonKey;
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            return "placeholder-anon-key";
        }

        // Kill-switch телеметрії через env (Конституція, Стаття 10).
        // Повний remote feature_flags/levels — пізніший слайс.
        private static bool IsTelemetryDisabled()
        {
            var value = System.Environment.GetEnvironmentVariable("SCLOCVERSE_TELEMETRY_DISABLED");
            return string.Equals(value, "1", StringComparison.Ordinal)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
