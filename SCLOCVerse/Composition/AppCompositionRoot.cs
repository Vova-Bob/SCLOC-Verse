using SCLOCVerse.Helpers;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Observability;
using SCLOCVerse.Services;
using SCLOCVerse.Services.ApplicationInstance;
using SCLOCVerse.Services.ApplicationUpdate;
using SCLOCVerse.Services.Autostart;
using SCLOCVerse.Services.Common;
using SCLOCVerse.Services.HangarTimer;
using SCLOCVerse.Services.InputSystem;
using SCLOCVerse.Services.LiaServices;
using SCLOCVerse.Services.LocalizationServices;
using SCLOCVerse.Services.Notifications;
using SCLOCVerse.Services.Observability;
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
            _hangarOverlayService = new HangarOverlayService(_hangarSettingsService);
            _hotkeyBackend = CreateHotkeyBackend();
            var diagnosticsEnabled = IsHotkeyDiagnosticsEnabled();
            _hotkeyService = new HotkeyService(_hotkeyBackend, diagnosticsEnabled);
            _hangarTimerService = new HangarTimerService(
                _hangarStartTimeProvider,
                _hangarOverlayService,
                _hangarSettingsService,
                _hotkeyService);

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
        }

        public void Dispose()
        {
            ShutdownTimingLogger.Log("AppCompositionRoot.Dispose START");

            // Спочатку зупиняємо телеметрію: її uploader використовує auth-клієнт,
            // тож глушимо до dispose auth-композиції (reverse-order).
            try { _telemetryClient?.Dispose(); } catch { /* ignore */ }

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

            if (_hangarTimerService is IDisposable hangarDisposable)
                hangarDisposable.Dispose();

            _authCompositionRoot?.Dispose();

            ShutdownTimingLogger.Log("AppCompositionRoot.Dispose END");
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
                _toastNotificationService);
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
