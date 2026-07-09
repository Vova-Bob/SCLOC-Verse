using SCLOCVerse.Controls;
using SCLOCVerse.Controls.Dialogs;
using SCLOCVerse.Helpers;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.ApplicationUpdate;
using SCLOCVerse.Models.Auth;
using SCLOCVerse.Models.LiaModels;
using SCLOCVerse.Models.Notifications;
using SCLOCVerse.Services;
using SCLOCVerse.Services.ApplicationUpdate;
using SCLOCVerse.Services.Cache;
using SCLOCVerse.Windows;
using SCLOCVerse.Services.InputSystem;
using SCLOCVerse.Services.LiaServices;
using SCLOCVerse.Services.Tray;
using SCLOCVerse.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Navigation;

namespace SCLOCVerse
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;
        private readonly IWindowHelper _windowHelper;
        private readonly ILocalizationInstaller _localizationInstaller;
        private readonly IReadmeService _readmeService;
        private readonly ICanvasManager _canvasManager;
        private readonly IButtonStateManager _buttonStateManager;
        private readonly IButtonHelper _buttonHelper;
        private readonly IToastService _toastService;
        private readonly ILinkService _linkService;
        private readonly IUpdater _updater;
        private readonly IApplicationUpdateService _applicationUpdateService;
        private readonly IBackgroundUpdateMonitor _backgroundUpdateMonitor;
        private readonly IUpdateChannelService _updateChannelService;
        private readonly IApplicationVersionProvider _applicationVersionProvider;
        private readonly IUpdateDownloader _updateDownloader;
        private readonly IUpdateInstaller _updateInstaller;
        private readonly IUpdateHistoryService _updateHistoryService;
        private readonly IUpdateVerifier _updateVerifier;
        private readonly IGitHubReleaseClient _gitHubReleaseClient;
        private readonly IDialogService _dialogService;
        private readonly IAuthService _authService;
        private readonly IAuthStatusProvider _authStatusProvider;
        private readonly AuthStatusPresenter _authStatusPresenter;
        private readonly UpdateStatusPresenter _updateStatusPresenter;
        private readonly IHangarTimerService _hangarTimerService;
        private readonly IHotkeyService _hotkeyService;
        private readonly ITrayService _trayService;
        private readonly IApplicationInstanceService _applicationInstanceService;
        private readonly IAutostartService _autostartService;
        private readonly IUiInteractionPolicy _uiPolicy;
        private readonly IPreferencesService _preferencesService;
        private readonly INotificationRouter _notificationRouter;
        private readonly IToastNotificationService _osToast;
        private IHotkeyMessageSource? _hotkeyMessageSource;
        private bool _showGameFolderToast = true;
        private DateTime? _suppressStartupUpdateCheckUntil;
        // Кешований статус Л.І.А з останнього GetStatusAsync. Використовується для
        // диспетчеризації кнопки BtnLiaInstall без зайвого мережевого запиту при кліці.
        private LiaInstallStatus? _lastLiaStatus;
        /// <summary>
        /// Прапець ідемпотентного запуску інтерактивного старту UI.
        /// Перший показ вікна (Tray/IPC) запускає CompleteInteractiveStartupAsync один раз.
        /// </summary>
        private bool _interactiveStartupCompleted;

        /// <summary>
        /// Прапорець відкладеного показу UpdateDialog.
        /// Встановлюється при BackgroundUiPolicy, коли знайдено оновлення, але модальний
        /// діалог не можна показати (він підняв би приховане вікно). Скидається при першому
        /// переході користувача у головне вікно (Tray_ShowRequested / IPC Show).
        /// НЕ показується при ShowLiaAssistant — користувач явно запросив LIA.
        /// </summary>
        private bool _isAppUpdateDialogPending;
        private EnvironmentSelector EnvSelector => CanvasLocalization.EnvironmentSelector;
        private Button BtnInstall => CanvasLocalization.InstallButton;
        private Button BtnLocalisationDelete => CanvasLocalization.DeleteButton;
        internal TextBox TxtLocalizationReadme => CanvasLocalization.ReadmeTextBox;
        private Button BtnReturnLocalizationHome => CanvasLocalization.ReturnHomeButton;
        private Button BtnAssistantReturnHome => CanvasAssistant.ReturnHomeButton;
        private Button BtnScToolsReturnHome => CanvasScTools.ReturnHomeButton;
        private Button BtnSelectFolder => CanvasSettings.SelectFolderButton;
        private Button BtnAutoSearch => CanvasSettings.AutoSearchButton;
        private Button BtnResetCash => CanvasSettings.ResetCacheButton;
        private Button BtnReturnHome => CanvasSettings.ReturnHomeButton;
        internal TextBox TxtSelectedPath => CanvasSettings.SelectedPathTextBox;
        internal TextBox TxtReadme => CanvasSettings.ReadmeTextBox;
        private Button BtnLiaInstall => CanvasAssistant.InstallButton;
        internal RichTextBox TxtLiaReadme => CanvasAssistant.ReadmeTextBox;
        internal TextBox TxtLiaSetupe => CanvasAssistant.SetupInfoTextBox;
        internal TextBox TxtLiaVersionPath => CanvasAssistant.TxtLiaVersionPath;
        private Button BtnLiaDelete => CanvasAssistant.BtnLiaDelete;

        private string? localFolder = string.Empty;
        public string DefaultPathText = string.Empty;
        public string MissingGameFolderToastText = string.Empty;
        private bool isSettingButtonClicked;
        private readonly UpdateCheckerService _updateCheckerService;
        private readonly CleanupController _cacheCleanupController;

        public MainWindow(MainWindowViewModel viewModel, IWindowHelper windowHelper, ILocalizationInstaller localizationInstaller, IReadmeService readmeService,     IUpdater updater, UpdateCheckerService updateCheckerService, IApplicationUpdateService applicationUpdateService, IBackgroundUpdateMonitor backgroundUpdateMonitor, IUpdateChannelService updateChannelService, IApplicationVersionProvider applicationVersionProvider, IUpdateDownloader updateDownloader, IUpdateInstaller updateInstaller, IUpdateHistoryService updateHistoryService, IUpdateVerifier updateVerifier, IGitHubReleaseClient gitHubReleaseClient, IDialogService dialogService, IAuthService authService, IAuthStatusProvider authStatusProvider, IHangarTimerService hangarTimerService, IHotkeyService hotkeyService, ITrayService trayService, IApplicationInstanceService applicationInstanceService, IAutostartService autostartService, IUiInteractionPolicy uiPolicy, IPreferencesService preferencesService, INotificationRouter notificationRouter, IToastNotificationService toastNotificationService)
        {
            InitializeComponent();

            _viewModel = viewModel;
            _windowHelper = windowHelper;
            _localizationInstaller = localizationInstaller;
            _readmeService = readmeService;
            _updater = updater;
            _updateCheckerService = updateCheckerService;
            _applicationUpdateService = applicationUpdateService;
            _backgroundUpdateMonitor = backgroundUpdateMonitor;
            _updateChannelService = updateChannelService;
            _applicationVersionProvider = applicationVersionProvider;
            _updateDownloader = updateDownloader;
            _updateInstaller = updateInstaller;
            _updateHistoryService = updateHistoryService;
            _updateVerifier = updateVerifier;
            _gitHubReleaseClient = gitHubReleaseClient;
            _dialogService = dialogService;
            _authService = authService;
            _authStatusProvider = authStatusProvider;
            _hangarTimerService = hangarTimerService;
            _hotkeyService = hotkeyService;
            _trayService = trayService;
            _applicationInstanceService = applicationInstanceService;
            _autostartService = autostartService;
            _uiPolicy = uiPolicy;
            _preferencesService = preferencesService;
            _notificationRouter = notificationRouter;
            _osToast = toastNotificationService;

            _toastService = new ToastService(AppToast.ToastBorder, AppToast.ToastText);
            _linkService = new LinkService(_toastService);
            _updateStatusPresenter = new UpdateStatusPresenter(
                CanvasHome.CurrentVersionTextControl,
                CanvasHome.AvailableVersionTextControl,
                CanvasHome.UpdateStatusTextControl,
                CanvasHome.UpdatePanel,
                CanvasHome.HideUpdatePanelStoryboard);

            var options = new CacheCleanupOptions();
            var inspector = new ShaderCacheInspector(options);
            var cleaner = new CacheCleaner(options);
            _cacheCleanupController = new CleanupController(inspector, cleaner, _toastService, Dispatcher);

            _buttonStateManager = new ButtonStateManager(BtnLocalization, BtnAssistant, BtnScTools, BtnSettings, BtnSelectFolder);
            _canvasManager = new CanvasManager(this, _buttonStateManager);
            _buttonHelper = new ButtonHelper();
            _authStatusPresenter = new AuthStatusPresenter(BtnAccount, _authStatusProvider);
            CanvasScTools.SetHangarTimerService(_hangarTimerService);

            // Auth Gate створюється програмно, оскільки потребує IAuthService через DI.
            var authGate = new AuthGateCanvas(_authService);
            AuthGateHost.Child = authGate;

            DataContext = _viewModel;
            DefaultPathText = TxtSelectedPath.Text;

            _buttonHelper.SetButtonState(BtnAutoSearch, _viewModel.IsGameFolderSet);


            CanvasHome.CurrentVersionTextControl.Text = _applicationVersionProvider.GetCurrentVersion().ToString();

            // Оркестратор піднімає UpdateCycleCompleted → NotificationRouter будує
            // NotificationCandidate[] → NotificationsReady → MainWindow маршрутизує
            // InApp (IToastService) vs OS Toast (IToastNotificationService) за видимістю.
            _notificationRouter.NotificationsReady += OnNotificationsReady;
            _backgroundUpdateMonitor.CheckFailed += OnBackgroundUpdateCheckFailed;

            CanvasSettings.UpdateChannelSelector.ItemsSource = new[]
            {
                new { DisplayName = "Стабільний", Value = UpdateChannel.Stable },
                new { DisplayName = "Тестовий", Value = UpdateChannel.Dev }
            };
            CanvasSettings.UpdateChannelSelector.SelectionChanged += UpdateChannelSelector_SelectionChanged;
            CanvasSettings.UpdateHistoryButtonControl.Click += UpdateHistoryButton_Click;

            // Settings Hub: ініціалізація внутрішньої навігації категорій.
            CanvasSettings.InitializeHubNavigation();

            // Підписка на чекбокси налаштувань (Етап D).
            // RunAtStartup синхронізується з реєстром (джерело істини), а не з Settings.
            CanvasSettings.RunAtStartupCheckBoxControl.Checked += RunAtStartupCheckBox_Changed;
            CanvasSettings.RunAtStartupCheckBoxControl.Unchecked += RunAtStartupCheckBox_Changed;
            CanvasSettings.MinimizeToTrayCheckBoxControl.Checked += MinimizeToTrayCheckBox_Changed;
            CanvasSettings.MinimizeToTrayCheckBoxControl.Unchecked += MinimizeToTrayCheckBox_Changed;
            CanvasSettings.AutoUpdateLocalizationCheckBoxControl.Checked += AutoUpdateLocalizationCheckBox_Changed;
            CanvasSettings.AutoUpdateLocalizationCheckBoxControl.Unchecked += AutoUpdateLocalizationCheckBox_Changed;
            CanvasSettings.AdvancedDiagnosticsCheckBoxControl.Checked += AdvancedDiagnosticsCheckBox_Changed;
            CanvasSettings.AdvancedDiagnosticsCheckBoxControl.Unchecked += AdvancedDiagnosticsCheckBox_Changed;

            Loaded += MainWindow_Loaded;
            _authService.StatusChanged += OnAuthStatusChanged;
            EnvSelector.GearClicked += EnvSelector_GearClicked;
            EnvSelector.SelectedEnvironmentChanged += (s, e) =>
            {
                BtnInstall.Content = _buttonHelper.GetInstallButtonText(EnvSelector?.SelectedEnvironment, _viewModel.GameFolder);
            };
            BtnAutoSearch.Loaded += (s, e) => _buttonHelper.SetButtonState(BtnAutoSearch, _viewModel.IsGameFolderSet);

            BtnReturnLocalizationHome.Click += ReturnToHome_Click;
            BtnAssistantReturnHome.Click += ReturnToHome_Click;
            BtnScToolsReturnHome.Click += ReturnToHome_Click;
            BtnReturnHome.Click += ReturnToLocalization_Click;

            BtnInstall.Click += BtnInstall_Click;
            BtnLocalisationDelete.Click += LocalisationDelete_Click;
            BtnSelectFolder.Click += BtnSelectFolder_Click;
            BtnAutoSearch.Click += BtnAutoSearch_Click;
            BtnResetCash.Click += BtnReset_Cash;
            BtnLiaInstall.Click += BtnLiaInstall_Click;
            BtnLiaDelete.Click += BtnLiaDelete_Click;

            // Ініціалізація системного трея та прив'язка його подій.
            // Tray працює незалежно від видимості вікна: приховане вікно
            // можна показати двойним кліком по іконці або через контекстне меню.
            _trayService.Initialize();
            _trayService.ShowRequested += Tray_ShowRequested;
            _trayService.CheckUpdatesRequested += Tray_CheckUpdatesRequested;
            _trayService.ExitRequested += Tray_ExitRequested;

            // Single Instance IPC: повторний запуск застосунку передає команду
            // через Named Pipe. Підписуємось на CommandReceived, щоб показати
            // вікно, коли користувач запускає другий екземпляр. Маршалінг у
            // UI-потік — через Dispatcher, бо подія приходить з pipe-сервера.
            _applicationInstanceService.CommandReceived += ApplicationInstance_CommandReceived;

            // BackgroundUpdateMonitor — єдина точка запуску життєвого циклу.
            // Конструктор викликається завжди (CompositionRoot.CreateMainWindow),
            // незалежно від window.Show()/window.Hide() (--minimized). Всі підписки
            // (UpdateCycleCompleted→Route, NotificationsReady, CheckFailed) вже виконані.
            // runImmediately забезпечує Toast про оновлення одразу після старту,
            // а не через 1 годину першого тику таймера.
            _backgroundUpdateMonitor.Start(runImmediately: true);
        }

        /// <summary>
        /// Обробник IPC-команд від повторних запусків.
        /// </summary>
        private void ApplicationInstance_CommandReceived(object? sender, Models.ApplicationInstance.InstanceCommand command)
        {
            // Команда приходить з фонового потоку pipe-сервера — маршалінг у UI.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                switch (command.Kind)
                {
                    case Models.ApplicationInstance.InstanceCommandKind.Show:
                        Show();
                        WindowState = WindowState.Normal;
                        Activate();
                        Focus();
                        // Перший показ вікна запускає відкладені промпти (якщо ще не виконувались).
                        _ = EnsureInteractiveUiInitializedAsync();
                        // Відкладений UpdateDialog, якщо знайшли оновлення під час фонового старту.
                        _ = ShowPendingAppUpdateDialogIfNeededAsync();
                        break;

                    case Models.ApplicationInstance.InstanceCommandKind.ShowLiaAssistant:
                        Show();
                        WindowState = WindowState.Normal;
                        Activate();
                        Focus();
                        _ = EnsureInteractiveUiInitializedAsync();
                        // Перехід на вкладку Assistant після завершення побудови UI —
                        // прибирає race condition на повільних ПК (DispatcherPriority.Loaded).
                        Dispatcher.BeginInvoke(
                            () => _canvasManager.SwitchCanvas(CanvasAssistant),
                            System.Windows.Threading.DispatcherPriority.Loaded);
                        break;
                }
            }));
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _windowHelper.ApplyWindowRoundCorners(this);
            MainGrid.MouseMove += (s, e2) => _windowHelper.HandleMouseMove(this, bgImage, e2.GetPosition(MainGrid), MainGrid);
            MainGrid.MouseLeave += (s, e2) => _windowHelper.HandleMouseLeave(this, bgImage, MainGrid);

            var helper = new WindowInteropHelper(this);
            _hotkeyMessageSource = new WpfMessageSource(this);
            _hotkeyService.InitializeBackend(_hotkeyMessageSource);

            _readmeService.LoadReadme(this);
            CanvasHome.ToastService = _toastService;
            CanvasHome.LinkService = _linkService;
            BtnAutoSearch.ApplyTemplate();
            BtnAutoSearch.IsEnabled = true;

            // Спочатку показуємо Auth Gate і перевіряємо сесію.
            // Перехід у Main UI відбудеться через OnAuthStatusChanged, якщо сесію відновлено.
            UpdateAppMode(_authService.State);
            var tasks = new Task[]
            {
                UpdateLiaVersionAsync(),
                _viewModel.InitializeAsync(),
                UpdateGameFolderUiAsync(_viewModel.GameFolder),
                RestoreAuthSessionAsync()
            };

            await Task.WhenAll(tasks).ConfigureAwait(true);

            InitializeUpdateChannel();
            InitializePreferenceCheckBoxes();

            // Стартові промпти — залежно від політики взаємодії з UI.
            // При --minimized (BackgroundUiPolicy) відкладаються до моменту
            // першого показу вікна користувачем (EnsureInteractiveUiInitializedAsync).
            if (_uiPolicy.CanShowStartupPrompts)
            {
                _ = EnsureInteractiveUiInitializedAsync();
            }
        }

        /// <summary>
        /// Єдина точка запуску інтерактивного старту UI. Ідемпотентна.
        /// Викликається:
        ///   - з MainWindow_Loaded, якщо політика дозволяє стартові промпти;
        ///   - з точок першого показу вікна (Tray_ShowRequested, IPC Show),
        ///     якщо раніше не виконувався (--minimized старт).
        /// </summary>
        private async Task EnsureInteractiveUiInitializedAsync()
        {
            // Non-reentrant guard: встановлюємо синхронно до будь-якого await,
            // щоб паралельні виклики з Tray та IPC не запустили промпти двічі.
            if (_interactiveStartupCompleted)
                return;
            _interactiveStartupCompleted = true;

            await CompleteInteractiveStartupAsync().ConfigureAwait(true);
        }

        /// <summary>
        /// Завершення інтерактивного старту: стартові тости, промпт кешу шейдерів,
        /// перевірка оновлень. Назва універсальна — сюди з часом можуть потрапити
        /// Welcome Wizard, What's New, Tips, Migration Dialog тощо.
        /// </summary>
        private async Task CompleteInteractiveStartupAsync()
        {
            _ = ShowStartupToastsAsync();
            _ = _cacheCleanupController.RunStartupPromptAsync(CancellationToken.None);

            _ = RunStartupUpdateCheckAsync();
            await Task.CompletedTask;
        }

        private bool _isInitializingUpdateChannel;

        private void InitializeUpdateChannel()
        {
            _isInitializingUpdateChannel = true;
            try
            {
                var currentChannel = _updateChannelService.GetUpdateChannel();
                if (Enum.TryParse<UpdateChannel>(currentChannel, out var channel))
                {
                    foreach (dynamic item in CanvasSettings.UpdateChannelSelector.Items)
                    {
                        if (item.Value == channel)
                        {
                            CanvasSettings.UpdateChannelSelector.SelectedItem = item;
                            break;
                        }
                    }
                }
                else
                {
                    CanvasSettings.UpdateChannelSelector.SelectedIndex = 0;
                }
            }
            finally
            {
                _isInitializingUpdateChannel = false;
            }
        }

        /// <summary>
        /// Прапець, що блокує підняття подій Checked/Unchecked під час програмної
        /// синхронізації стану чекбоксів (патерн як _isInitializingUpdateChannel).
        /// </summary>
        private bool _isInitializingPreferences;

        /// <summary>
        /// Синхронізує стан чекбоксів налаштувань зі джерелами істини:
        ///   - RunAtStartup       ← IAutostartService.IsEnabled() (реєстр)
        ///   - MinimizeToTray     ← IPreferencesService.GetMinimizeToTray()
        ///   - AutoUpdateLoc      ← IPreferencesService.GetAutoUpdateLocalization()
        /// </summary>
        private void InitializePreferenceCheckBoxes()
        {
            _isInitializingPreferences = true;
            try
            {
                // RunAtStartup: джерело істини — реєстр (можна змінити через Task Manager).
                CanvasSettings.RunAtStartupCheckBoxControl.IsChecked = _autostartService.IsEnabled();
                CanvasSettings.MinimizeToTrayCheckBoxControl.IsChecked = _preferencesService.GetMinimizeToTray();
                CanvasSettings.AutoUpdateLocalizationCheckBoxControl.IsChecked = _preferencesService.GetAutoUpdateLocalization();
                CanvasSettings.AdvancedDiagnosticsCheckBoxControl.IsChecked = _preferencesService.GetAdvancedDiagnostics();
            }
            finally
            {
                _isInitializingPreferences = false;
            }
        }

        private void RunAtStartupCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializingPreferences)
                return;

            var isChecked = CanvasSettings.RunAtStartupCheckBoxControl.IsChecked == true;
            try
            {
                if (isChecked)
                    _autostartService.Enable();
                else
                    _autostartService.Disable();
            }
            catch (Exception ex)
            {
                // Реєстр недоступний — відкочуємо стан чекбокса до фактичного.
                System.Diagnostics.Debug.WriteLine($"[RunAtStartup] {ex.Message}");
                _isInitializingPreferences = true;
                CanvasSettings.RunAtStartupCheckBoxControl.IsChecked = _autostartService.IsEnabled();
                _isInitializingPreferences = false;
            }
        }

        private void MinimizeToTrayCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializingPreferences)
                return;

            _preferencesService.SetMinimizeToTray(CanvasSettings.MinimizeToTrayCheckBoxControl.IsChecked == true);
        }

        private void AutoUpdateLocalizationCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializingPreferences)
                return;

            _preferencesService.SetAutoUpdateLocalization(CanvasSettings.AutoUpdateLocalizationCheckBoxControl.IsChecked == true);
        }

        private void AdvancedDiagnosticsCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializingPreferences)
                return;

            _preferencesService.SetAdvancedDiagnostics(CanvasSettings.AdvancedDiagnosticsCheckBoxControl.IsChecked == true);
        }

        private async Task RunManualUpdateCheckAsync(bool forceRefresh = false)
        {
            var currentVersion = _applicationVersionProvider.GetCurrentVersion();
            _updateStatusPresenter.ShowChecking(currentVersion);

            try
            {
                var result = await _applicationUpdateService.CheckForUpdatesAsync(forceRefresh, CancellationToken.None).ConfigureAwait(true);
                await ApplyUpdateCheckResultAsync(result).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _updateStatusPresenter.ShowCheckFailed(new UpdateCheckResult { Message = ex.Message });
                await _toastService.ShowToastAsync($"Помилка: {ex.Message}").ConfigureAwait(true);
            }
        }

        private async Task ApplyUpdateCheckResultAsync(UpdateCheckResult result)
        {
            switch (result.Status)
            {
                case UpdateCheckStatus.UpToDate:
                    await _updateStatusPresenter.ShowUpToDateAsync(result).ConfigureAwait(true);
                    await _updateHistoryService.AddEntryAsync(CreateHistoryEntry(UpdateOperation.Check, UpdateOperationResult.Success, result)).ConfigureAwait(true);
                    await _toastService.ShowToastAsync("Ви використовуєте актуальну версію.").ConfigureAwait(true);
                    break;

                case UpdateCheckStatus.UpdateAvailable:
                    _updateStatusPresenter.ShowUpdateAvailable(result);
                    await _updateHistoryService.AddEntryAsync(CreateHistoryEntry(UpdateOperation.Check, UpdateOperationResult.Success, result)).ConfigureAwait(true);

                    // Background startup (--minimized / автозапуск): модальний діалог не показуємо,
                    // бо він підніме приховане вікно. App Toast йде через BackgroundUpdateMonitor
                    // → NotificationRouter → PresentNotification (існуючий єдиний канал сповіщень).
                    // Діалог відкладається до першого переходу користувача у головне вікно.
                    if (!_uiPolicy.CanShowModalDialogs)
                        break;

                    await ShowUpdateDialogAndInstallAsync(result).ConfigureAwait(true);
                    break;

                case UpdateCheckStatus.CheckFailed:
                    _updateStatusPresenter.ShowCheckFailed(result);
                    await _updateHistoryService.AddEntryAsync(CreateHistoryEntry(UpdateOperation.Check, UpdateOperationResult.Failed, result, result.Message)).ConfigureAwait(true);
                    await _toastService.ShowToastAsync($"Помилка: {result.Message}").ConfigureAwait(true);
                    break;

                case UpdateCheckStatus.ChannelNotFound:
                    _updateStatusPresenter.ShowChannelNotFound(result);
                    await _updateHistoryService.AddEntryAsync(CreateHistoryEntry(UpdateOperation.Check, UpdateOperationResult.Skipped, result, "Не знайдено релізів для поточного каналу.")).ConfigureAwait(true);
                    await _toastService.ShowToastAsync("Для поточного каналу не знайдено релізів.").ConfigureAwait(true);
                    break;
            }
        }

        /// <summary>
        /// Показує модальний UpdateDialog і за підтвердженням запускає встановлення.
        /// Єдина точка логіки діалогу — використовується як інтерактивним шляхом
        /// (ApplyUpdateCheckResultAsync), так і відкладеним (ShowPendingAppUpdateDialogIfNeededAsync).
        /// </summary>
        private async Task ShowUpdateDialogAndInstallAsync(UpdateCheckResult result)
        {
            var confirmed = await _dialogService.ShowUpdateDialogAsync(result.LatestVersion.ToString(), this).ConfigureAwait(true);

            if (confirmed)
            {
                await InstallUpdateAsync(result).ConfigureAwait(true);
            }
            else
            {
                await _updateStatusPresenter.ShowUpdateCancelledAsync(result).ConfigureAwait(true);
                await _updateHistoryService.AddEntryAsync(CreateHistoryEntry(UpdateOperation.Install, UpdateOperationResult.Cancelled, result, "Користувач відмовився від встановлення.")).ConfigureAwait(true);
            }
        }

        /// <summary>
        /// Відкладений показ UpdateDialog після переходу користувача у головне вікно
        /// (Tray_ShowRequested / IPC Show). Не викликається з ShowLiaAssistant.
        /// Скидає прапорець синхронно до await — ідемпотентно проти паралельних викликів.
        /// </summary>
        private async Task ShowPendingAppUpdateDialogIfNeededAsync()
        {
            if (!_isAppUpdateDialogPending)
                return;

            _isAppUpdateDialogPending = false;

            // Результат беремо з кешу (TTL 30 хв) — без зайвого HTTP.
            // Якщо кеш протух або оновлення вже не актуальне — діалог не показуємо.
            var result = await _applicationUpdateService.CheckForUpdatesAsync(forceRefresh: false, CancellationToken.None).ConfigureAwait(true);
            if (result?.IsUpdateAvailable != true)
                return;

            await ShowUpdateDialogAndInstallAsync(result).ConfigureAwait(true);
        }

        private async Task RunStartupUpdateCheckAsync()
        {
            await Task.Delay(UpdateConstants.StartupUpdateCheckDelay).ConfigureAwait(true);

            // BackgroundUpdateMonitor стартує в конструкторі MainWindow (єдине місце),
            // тут лише Manual-перевірка з модальним діалогом (лише інтерактивний старт).
            if (_suppressStartupUpdateCheckUntil.HasValue && DateTime.Now < _suppressStartupUpdateCheckUntil.Value)
                return;

            await RunManualUpdateCheckAsync(forceRefresh: false).ConfigureAwait(true);
        }

        /// <summary>
        /// Обробник NotificationsReady від NotificationRouter.
        /// Event може піднятись з фонового потоку — маршалізуємо в UI через Dispatcher.CheckAccess.
        /// </summary>
        private void OnNotificationsReady(object? sender, IReadOnlyList<NotificationCandidate> candidates)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnNotificationsReady(sender, candidates)));
                return;
            }

            foreach (var candidate in candidates)
            {
                // App Update знайдено через BackgroundUpdateMonitor (Pipeline 1).
                // При BackgroundUiPolicy модальний діалог не можна показати (вікно приховане),
                // тож позначаємо потребу в відкладеному діалозі ДО показу Toast.
                // Це усуває race: прапорець встановлюється синхронно до PresentNotification,
                // а не через ~1с у Manual-пайплайні (Task.Delay у RunStartupUpdateCheckAsync).
                if (candidate.Source == NotificationSource.Application
                    && !_uiPolicy.CanShowModalDialogs)
                {
                    _isAppUpdateDialogPending = true;
                }

                PresentNotification(candidate);
            }
        }

        /// <summary>
        /// Вибирає канал доставки за NotificationPolicy + видимістю вікна.
        /// Изольований метод — майбутнє розширення (Discord, sound, flash) додається тут,
        /// не розростанням OnNotificationsReady.
        /// </summary>
        private void PresentNotification(NotificationCandidate candidate)
        {
            if (candidate.Policy == NotificationPolicy.Silent)
                return;

            var useOs = candidate.Policy == NotificationPolicy.OS
                || (candidate.Policy == NotificationPolicy.Auto && !IsWindowVisible());

            if (useOs)
            {
                _osToast.Show(new ToastNotification
                {
                    Title = "SCLOC-Verse",
                    Message = candidate.Message,
                    SourceTag = candidate.Source switch
                    {
                        NotificationSource.Application => ToastSources.AppUpdate,
                        NotificationSource.Localization => ToastSources.Localization,
                        NotificationSource.Lia => ToastSources.Lia,
                        _ => null
                    },
                    Severity = candidate.Severity
                });
            }
            else
            {
                _ = _toastService.ShowToastAsync(candidate.Message);
            }
        }

        /// <summary>
        /// Чи видиме головне вікно (не Hidden/Collapsed, не Minimized).
        /// Використовується NotificationRouter-обробником для маршрутизації InApp vs OS Toast.
        /// </summary>
        private bool IsWindowVisible()
            => Visibility == Visibility.Visible && WindowState != WindowState.Minimized;

        private void OnBackgroundUpdateCheckFailed(object? sender, Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"[BackgroundUpdateCheckFailed] {exception}");
        }

        private async Task InstallUpdateAsync(UpdateCheckResult result)
        {
            var updateDirectory = Path.Combine(
                Path.GetTempPath(),
                UpdateConstants.UpdateDirectoryName,
                UpdateConstants.UpdatesDirectoryName);

            if (!Directory.Exists(updateDirectory))
                Directory.CreateDirectory(updateDirectory);

            foreach (var existingFile in Directory.EnumerateFiles(updateDirectory, "*.exe"))
            {
                try { File.Delete(existingFile); }
                catch { /* ігноруємо помилки очищення старих файлів */ }
            }

            using var cts = new CancellationTokenSource();
            var progressWindow = new AppUpdateProgressWindow { Owner = this };
            progressWindow.CancelRequested += (_, _) => cts.Cancel();
            progressWindow.SetStage("Завантаження оновлення...");
            progressWindow.Show();

            string installerPath;
            try
            {
                var progress = new Progress<UpdateDownloadProgress>(p => progressWindow.Report(p));

                installerPath = await _updateDownloader.DownloadAsync(
                    result.DownloadUrl,
                    updateDirectory,
                    progress,
                    cts.Token).ConfigureAwait(true);

                await _updateHistoryService.AddEntryAsync(CreateHistoryEntry(UpdateOperation.Download, UpdateOperationResult.Success, result)).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                await _updateHistoryService.AddEntryAsync(CreateHistoryEntry(UpdateOperation.Download, UpdateOperationResult.Failed, result, "Завантаження скасовано користувачем.")).ConfigureAwait(true);
                progressWindow.Close();
                return;
            }
            catch (Exception ex)
            {
                await _updateHistoryService.AddEntryAsync(CreateHistoryEntry(UpdateOperation.Download, UpdateOperationResult.Failed, result, ex.Message)).ConfigureAwait(true);
                CanvasHome.UpdateStatusTextControl.Text = "Помилка завантаження";
                CanvasHome.UpdateStatusTextControl.Foreground = Brushes.Red;
                await _toastService.ShowToastAsync($"Помилка завантаження: {ex.Message}").ConfigureAwait(true);
                progressWindow.Close();
                return;
            }

            progressWindow.SetStage("✓ Завантаження завершено\nПеревірка цілісності...");

            try
            {
                if (string.IsNullOrWhiteSpace(result.ExpectedChecksum))
                {
                    await _updateHistoryService.AddEntryAsync(CreateHistoryEntry(UpdateOperation.Verify, UpdateOperationResult.Skipped, result, "Контрольна сума відсутня.")).ConfigureAwait(true);
                }
                else
                {
                    var isValid = await _updateVerifier.VerifyAsync(
                        installerPath,
                        result.ExpectedChecksum,
                        cts.Token).ConfigureAwait(true);

                    if (!isValid)
                    {
                        await _updateHistoryService.AddEntryAsync(CreateHistoryEntry(UpdateOperation.Verify, UpdateOperationResult.Failed, result, "Невідповідність контрольної суми.")).ConfigureAwait(true);
                        CanvasHome.UpdateStatusTextControl.Text = "Помилка перевірки файлу";
                        CanvasHome.UpdateStatusTextControl.Foreground = Brushes.Red;
                        await _toastService.ShowToastAsync("Помилка перевірки файлу оновлення.").ConfigureAwait(true);
                        progressWindow.Close();
                        return;
                    }

                    await _updateHistoryService.AddEntryAsync(CreateHistoryEntry(UpdateOperation.Verify, UpdateOperationResult.Success, result)).ConfigureAwait(true);
                }
            }
            catch (OperationCanceledException)
            {
                await _updateHistoryService.AddEntryAsync(CreateHistoryEntry(UpdateOperation.Verify, UpdateOperationResult.Failed, result, "Перевірку скасовано користувачем.")).ConfigureAwait(true);
                progressWindow.Close();
                return;
            }
            catch (Exception ex)
            {
                await _updateHistoryService.AddEntryAsync(CreateHistoryEntry(UpdateOperation.Verify, UpdateOperationResult.Failed, result, ex.Message)).ConfigureAwait(true);
                CanvasHome.UpdateStatusTextControl.Text = "Помилка перевірки файлу";
                CanvasHome.UpdateStatusTextControl.Foreground = Brushes.Red;
                await _toastService.ShowToastAsync($"Помилка перевірки файлу: {ex.Message}").ConfigureAwait(true);
                progressWindow.Close();
                return;
            }

            progressWindow.SetStage("✓ Завантаження завершено\n✓ Перевірку завершено\nЗапуск інсталятора...");

            try
            {
                var currentExePath = Environment.ProcessPath
                    ?? throw new InvalidOperationException("Unable to determine executable path.");

                var installStarted = await _updateInstaller.InstallAsync(
                    installerPath,
                    currentExePath,
                    cts.Token).ConfigureAwait(true);

                if (installStarted)
                {
                    await _updateHistoryService.AddEntryAsync(CreateHistoryEntry(UpdateOperation.Install, UpdateOperationResult.Success, result)).ConfigureAwait(true);
                    progressWindow.MarkCompleted("✓ Завантаження завершено\n✓ Перевірку завершено\nЗапуск інсталятора...");
                    await _toastService.ShowToastAsync("Оновлення встановлюється. Додаток буде перезапущено.", 2000).ConfigureAwait(true);
                    await Task.Delay(800, cts.Token).ConfigureAwait(true);
                    Application.Current.Shutdown();
                }
                else
                {
                    await _updateHistoryService.AddEntryAsync(CreateHistoryEntry(UpdateOperation.Install, UpdateOperationResult.Failed, result, "Не вдалося запустити процес встановлення.")).ConfigureAwait(true);
                    await _toastService.ShowToastAsync("Не вдалося запустити встановлення оновлення.").ConfigureAwait(true);
                    progressWindow.Close();
                }
            }
            catch (OperationCanceledException)
            {
                await _updateHistoryService.AddEntryAsync(CreateHistoryEntry(UpdateOperation.Install, UpdateOperationResult.Failed, result, "Встановлення скасовано користувачем.")).ConfigureAwait(true);
                progressWindow.Close();
            }
            catch (Exception ex)
            {
                await _updateHistoryService.AddEntryAsync(CreateHistoryEntry(UpdateOperation.Install, UpdateOperationResult.Failed, result, ex.Message)).ConfigureAwait(true);
                CanvasHome.UpdateStatusTextControl.Text = "Помилка встановлення";
                CanvasHome.UpdateStatusTextControl.Foreground = Brushes.Red;
                await _toastService.ShowToastAsync($"Помилка встановлення: {ex.Message}").ConfigureAwait(true);
                progressWindow.Close();
            }
        }

        private UpdateHistoryEntry CreateHistoryEntry(
            UpdateOperation operation,
            UpdateOperationResult result,
            UpdateCheckResult checkResult,
            string errorMessage = "")
        {
            return new UpdateHistoryEntry
            {
                Timestamp = DateTimeOffset.Now,
                Channel = checkResult.Channel,
                FromVersion = checkResult.CurrentVersion,
                ToVersion = checkResult.LatestVersion,
                Operation = operation,
                Result = result,
                ErrorMessage = errorMessage
            };
        }

        private void UpdateChannelSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Ігноруємо події, що відбуваються під час ініціалізації UI,
            // щоб не перезаписати значення, перенесене з попередньої версії.
            if (_isInitializingUpdateChannel)
                return;

            dynamic selected = CanvasSettings.UpdateChannelSelector.SelectedItem;
            if (selected != null)
            {
                _updateChannelService.SetUpdateChannel(selected.Value.ToString());
            }
        }

        private void UpdateHistoryButton_Click(object sender, RoutedEventArgs e)
        {
                var window = new UpdateHistoryWindow(
                    _applicationUpdateService,
                    _gitHubReleaseClient,
                    _applicationVersionProvider,
                    _updateChannelService,
                    _linkService,
                    _dialogService,
                    release => _ = InstallReleaseAsync(release))
            {
                Owner = this
            };

            window.ShowDialog();
        }

        private async Task InstallReleaseAsync(GitHubRelease release)
        {
            try
            {
                var version = VersionParser.Parse(release.TagName);
                var asset = release.Assets.FirstOrDefault(a => a.Name.Equals(UpdateConstants.SetupAssetName, StringComparison.OrdinalIgnoreCase));
                if (asset == null)
                {
                    await _toastService.ShowToastAsync("Не знайдено інсталятор у релізі.").ConfigureAwait(true);
                    return;
                }

                var checksumAsset = release.Assets.FirstOrDefault(a => a.Name.Equals(UpdateConstants.ChecksumAssetName, StringComparison.OrdinalIgnoreCase));
                string checksum = string.Empty;
                if (checksumAsset != null)
                {
                    try
                    {
                        checksum = await _gitHubReleaseClient.DownloadTextAsync(checksumAsset.BrowserDownloadUrl, CancellationToken.None).ConfigureAwait(true);
                        checksum = checksum.Trim().Split(' ').FirstOrDefault() ?? string.Empty;
                    }
                    catch { /* ігноруємо помилку завантаження checksum */ }
                }

                var result = new UpdateCheckResult
                {
                    IsUpdateAvailable = true,
                    CurrentVersion = _applicationVersionProvider.GetCurrentVersion(),
                    LatestVersion = version,
                    DownloadUrl = asset.BrowserDownloadUrl,
                    ExpectedChecksum = checksum,
                    ReleaseNotes = release.Body,
                    Channel = release.Prerelease ? UpdateChannel.Dev : UpdateChannel.Stable,
                    Status = UpdateCheckStatus.UpdateAvailable
                };

                // Пригнічуємо автоматичну перевірку оновлень при наступному запуску,
                // щоб тільки що встановлена версія не пропонувала оновитися одразу після перезапуску.
                _suppressStartupUpdateCheckUntil = DateTime.Now.AddMinutes(5);

                await InstallUpdateAsync(result).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                await _toastService.ShowToastAsync($"Помилка встановлення версії: {ex.Message}").ConfigureAwait(true);
            }
        }

        private void EnvSelector_SelectionChanged(object? sender, EventArgs e)
        {
            _buttonHelper.GetInstallButtonText(EnvSelector?.SelectedEnvironment, _viewModel.GameFolder);
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _windowHelper.DragWindow(this, e);
        }

        private async void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            await _linkService.OpenLinkAsync(e.Uri.AbsoluteUri).ConfigureAwait(true);
            e.Handled = true;
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        /// <summary>
        /// Кнопка X у title bar. Єдина політика закриття (правка #5): перевіряє
        /// MinimizeToTray через IPreferencesService.
        ///   MinimizeToTray=true  → MinimizeToTray() (Hide у трей)
        ///   MinimizeToTray=false → Close() → OnClosed → Shutdown (повний вихід)
        /// </summary>
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            HandleCloseRequested();
        }

        /// <summary>
        /// Єдина точка прийняття рішення про закриття. Викликається з:
        ///   - Close_Click (X-кнопка)
        ///   - OnClosing (Alt+F4, Taskbar → Close)
        /// Гарантує однакову поведінку для всіх способів закриття.
        /// </summary>
        private void HandleCloseRequested()
        {
            if (_preferencesService.GetMinimizeToTray())
                MinimizeToTray();
            else
                Close();
        }

        /// <summary>
        /// Приховує вікно в системний трей. Не завершує процес.
        /// </summary>
        private void MinimizeToTray()
        {
            Hide();
        }

        /// <summary>
        /// Системне закриття (Alt+F4, Taskbar → Close).
        /// Єдина політика: якщо MinimizeToTray=true — відміняємо закриття й ховаємо в трей.
        /// Інакше — передаємо стандартному WPF-шляху → OnClosed → Shutdown.
        ///
        /// Tray-меню "Вийти" обходить це: там встановлюється явний виклик Close()
        /// через інший шлях (через _isExiting — прибрано в SessionFix, перевіряємо
        /// через прапець _forceExit).
        /// </summary>
        private bool _forceExit;

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_forceExit)
            {
                // Явний вихід через tray "Вийти" — стандартний шлях закриття.
                base.OnClosing(e);
                return;
            }

            if (_preferencesService.GetMinimizeToTray())
            {
                // Згортаємо в трей замість виходу.
                e.Cancel = true;
                MinimizeToTray();
                return;
            }

            base.OnClosing(e);
        }

        /// <summary>
        /// OnClosed — остання точка перед знищенням вікна.
        /// Критична деталь: ShutdownMode = OnExplicitShutdown (App.xaml.cs),
        /// тож стандартне закриття вікна НЕ гасить процес автоматично.
        /// Тому тут, у OnClosed, викликається явний Application.Shutdown().
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            // Явне завершення застосунку. Запускає App.OnExit → CompositionRoot.Dispose
            // (reverse-order: telemetry, tray, background monitor, overlay, hangar, auth).
            Application.Current.Shutdown();
        }

        /// <summary>Tray: показати вікно та активувати його.</summary>
        private void Tray_ShowRequested(object? sender, EventArgs e)
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            Focus();
            // Перший показ вікна запускає відкладені промпти (якщо ще не виконувались).
            _ = EnsureInteractiveUiInitializedAsync();
            // Відкладений UpdateDialog, якщо знайшли оновлення під час фонового старту.
            _ = ShowPendingAppUpdateDialogIfNeededAsync();
        }

        /// <summary>Tray: ініціювати ручну перевірку оновлень застосунку.</summary>
        private void Tray_CheckUpdatesRequested(object? sender, EventArgs e)
        {
            // Show вікно, щоб користувач бачив результат перевірки.
            Show();
            WindowState = WindowState.Normal;
            Activate();
            // Перший показ вікна запускає відкладені промпти (якщо ще не виконувались).
            _ = EnsureInteractiveUiInitializedAsync();
            _ = RunManualUpdateCheckAsync(forceRefresh: true);
        }

        /// <summary>Tray: повний вихід із застосунку через tray-меню "Вийти".</summary>
        private void Tray_ExitRequested(object? sender, EventArgs e)
        {
            // Прапець _forceExit знімає перехоплення OnClosing, щоб Close()
            // реально завершив процес (а не згорнув у трей за MinimizeToTray).
            _forceExit = true;
            Close();
        }

        private void Account_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_authService.State != AuthState.SignedIn)
                    return;

                AccountDialog.Show(this, _authService);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Account_Click error: {ex}", "Debug", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnAuthStatusChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() => UpdateAppMode(_authService.State));
        }

        /// <summary>
        /// Перемикає застосунок між Auth Gate Mode та Main UI Mode залежно від AuthState.
        /// </summary>
        private async void UpdateAppMode(AuthState state)
        {
            if (state == AuthState.SignedIn)
            {
                await ShowMainUiModeAsync();
            }
            else
            {
                ShowAuthGateMode();
            }
        }

        private void ShowAuthGateMode()
        {
            // Скидаємо Opacity Auth Gate, оскільки попередній fade out міг залишити його 0.
            if (AuthGateHost.Child is AuthGateCanvas authGate)
            {
                authGate.Opacity = 1;
                authGate.BeginAnimation(OpacityProperty, null);
            }

            AuthGateHost.Visibility = Visibility.Visible;
            MainContentContainer.Visibility = Visibility.Collapsed;
            MenuPanel.IsEnabled = false;
            BtnAccount.Visibility = Visibility.Collapsed;
        }

        private async Task ShowMainUiModeAsync()
        {
            if (AuthGateHost.Child is AuthGateCanvas authGate)
            {
                await authGate.FadeOutAsync(300).ConfigureAwait(true);
            }

            AuthGateHost.Visibility = Visibility.Collapsed;
            MainContentContainer.Visibility = Visibility.Visible;
            MenuPanel.IsEnabled = true;
            BtnAccount.Visibility = Visibility.Visible;
            _canvasManager.ShowCanvas("home");
        }

        private async Task RestoreAuthSessionAsync()
        {
            try
            {
                await _authService.TryRestoreSessionAsync(CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RestoreAuthSessionAsync] {ex}");
            }
        }
        private void About_Click(object sender, RoutedEventArgs e)
        {
            var version = _applicationVersionProvider.GetCurrentVersion().ToString();
            AboutDialog.Show(this, version);
        }

        private void Localization_Click(object sender, RoutedEventArgs e)
        {
            _canvasManager.SwitchCanvas(CanvasLocalization);
            isSettingButtonClicked = false;
        }

        private async void Assistant_Click(object sender, RoutedEventArgs e)
        {
            _canvasManager.SwitchCanvas(CanvasAssistant);
            isSettingButtonClicked = false;

            await UpdateLiaVersionAsync();
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            _canvasManager.SwitchCanvas(CanvasSettings);
            isSettingButtonClicked = true;
        }

        /// <summary>
        /// F1 відкриває Settings Hub — лише коли головне вікно SCLOC-Verse активне
        /// (PreviewKeyDown спрацьовує тільки при фокусі клавіатури на вікні).
        /// Глобальна реєстрація F1 навмисно відсутня, щоб не конфліктувати з
        /// ігровою/системною довідкою.
        /// </summary>
        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F1)
            {
                _canvasManager.SwitchCanvas(CanvasSettings);
                isSettingButtonClicked = true;
                e.Handled = true;
            }
        }

        private void ScTools_Click(object sender, RoutedEventArgs e)
        {
            _canvasManager.SwitchCanvas(CanvasScTools);
            isSettingButtonClicked = false;
        }

        private void LocalisationSettings_Click(object sender, RoutedEventArgs e)
        {
            _canvasManager.SwitchCanvas(CanvasSettings);
        }

        private void ReturnToHome_Click(object sender, RoutedEventArgs e)
        {
            _canvasManager.SwitchCanvas(CanvasHome);
            isSettingButtonClicked = false;
        }

        private void ReturnToLocalization_Click(object sender, RoutedEventArgs e)
        {
            if (isSettingButtonClicked)
            {
                _canvasManager.SwitchCanvas(CanvasHome, "home");
                isSettingButtonClicked = false;
            }
            else
            {
                _canvasManager.SwitchCanvas(CanvasLocalization, "localization");
            }
        }

        private void ReturnToAssistant_Click(object sender, RoutedEventArgs e)
        {
            _canvasManager.SwitchCanvas(CanvasAssistant);
        }

        private void EnvSelector_GearClicked(object? sender, EventArgs e)
        {
            _canvasManager.SwitchCanvas(CanvasSettings);
        }

        private async void BtnSelectFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (_viewModel.TrySetGameFolder(dialog.SelectedPath))
                {
                    await UpdateGameFolderUiAsync(_viewModel.GameFolder).ConfigureAwait(true);
                    await _toastService.ShowToastAsync($"Вибрано папку: {_viewModel.GameFolder}").ConfigureAwait(true);
                }
                else
                {
                    await _toastService.ShowToastAsync("Обраний шлях не існує. Спробуйте інший.").ConfigureAwait(true);
                }
            }
        }

        private async void BtnAutoSearch_Click(object sender, RoutedEventArgs e)
        {
            _showGameFolderToast = false;
            BtnAutoSearch.ApplyTemplate();

            if (!_viewModel.IsGameFolderSet)
            {
                var foundFolder = await _viewModel.DetectGameFolderAsync(4, CancellationToken.None);
                if (!string.IsNullOrEmpty(foundFolder))
                {
                    await UpdateGameFolderUiAsync(_viewModel.GameFolder).ConfigureAwait(true);
                    await _toastService.ShowToastAsync($"Знайдено папку: {foundFolder}").ConfigureAwait(true);
                }
                else
                {
                    await _toastService.ShowToastAsync("Не вдалося знайти папку. Будь ласка, оберіть вручну.").ConfigureAwait(true);
                    return;
                }
            }
            else
            {
                _viewModel.ResetGameFolder();
                await UpdateGameFolderUiAsync(null).ConfigureAwait(true);
                await _toastService.ShowToastAsync("Збережений шлях успішно скинуто.").ConfigureAwait(true);
            }
        }

        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            if (!EnvSelector.TryGetSelectedEnvironment(out var env, out var folderPath, out var envName, "встановлення локалізації"))
            {
                await _toastService.ShowToastAsync("Будь ласка, оберіть середовище та переконайтесь, що папка існує.").ConfigureAwait(true);
                return;
            }

            BtnInstall.IsEnabled = false;

            try
            {
                var result = await _localizationInstaller.InstallAsync(folderPath!, envName!);
                await _toastService.ShowToastAsync(result.Message).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                await _toastService.ShowToastAsync($"Помилка: {ex.Message}").ConfigureAwait(true);
            }
            finally
            {
                BtnInstall.IsEnabled = true;
                BtnInstall.Content = _buttonHelper.GetInstallButtonText(env, _viewModel.GameFolder);
            }
        }

        private async void LocalisationDelete_Click(object sender, RoutedEventArgs e)
        {
            var env = EnvSelector.SelectedEnvironment;

            if (env == null || string.IsNullOrWhiteSpace(env.FolderPath))
            {
                await _toastService.ShowToastAsync("Будь ласка, оберіть середовище для видалення локалізації.").ConfigureAwait(true);
                return;
            }

            try
            {
                var result = await _localizationInstaller.DeleteAsync(env.FolderPath, env.Name);

                BtnInstall.Content = _buttonHelper.GetInstallButtonText(EnvSelector?.SelectedEnvironment, _viewModel.GameFolder);
                await _toastService.ShowToastAsync(result.Message).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                await _toastService.ShowToastAsync($"Помилка при видаленні локалізації: {ex.Message}").ConfigureAwait(true);
            }
        }

        private async void BtnLiaInstall_Click(object sender, RoutedEventArgs e)
        {
            // Диспетчеризація за кешованим статусом (нуль мережі при кліці):
            //  - не встановлено / доступне оновлення → InstallLatestAsync
            //  - встановлено + актуально (або Orange) → LaunchAsync (локальний запуск)
            if (_lastLiaStatus is { IsInstalled: true, IsUpdateAvailable: false })
            {
                await LaunchLiaAsync();
                return;
            }

            await InstallLiaAsync();
        }

        /// <summary>
        /// Локальний запуск встановленого пакунка Л.І.А через shell:AppsFolder.
        /// Мережа не використовується — лише локальна активація AppX.
        /// </summary>
        private async Task LaunchLiaAsync()
        {
            BtnLiaInstall.IsEnabled = false;
            BtnLiaDelete.IsEnabled = false;

            try
            {
                await _updater.LaunchAsync().ConfigureAwait(true);
                await _toastService.ShowToastAsync("Л.І.А запущено.").ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                TxtLiaSetupe.Text += $"\nПомилка запуску: {ex.Message}";
                await _toastService.ShowToastAsync("Не вдалося запустити Л.І.А.").ConfigureAwait(true);
            }
            finally
            {
                BtnLiaInstall.IsEnabled = true;
                BtnLiaDelete.IsEnabled = true;
            }
        }

        /// <summary>
        /// Інсталяція або оновлення Л.І.А з GitHub. Тост розрізняє встановлення
        /// та оновлення за статусом до початку операції (_lastLiaStatus.IsInstalled).
        /// </summary>
        private async Task InstallLiaAsync()
        {
            TxtLiaSetupe.Text = string.Empty;
            BtnLiaInstall.IsEnabled = false;
            BtnLiaDelete.IsEnabled = false;

            // Фіксуємо стан до інсталяції: true = було встановлено (значить — оновлення),
            // false = не було (значить — нова інсталяція).
            bool wasInstalled = _lastLiaStatus?.IsInstalled ?? false;

            try
            {
                Action<string> logCallback = msg =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtLiaSetupe.Text += $"{msg}\n";
                        TxtLiaSetupe.ScrollToEnd();
                    });
                };

                await _updater.InstallLatestAsync(logCallback).ConfigureAwait(true);

                string toastMessage = wasInstalled
                    ? "Л.І.А успішно оновлено."
                    : "Л.І.А успішно встановлено.";
                await _toastService.ShowToastAsync(toastMessage).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                TxtLiaSetupe.Text += $"\nПомилка: {ex.Message}";
                await _toastService.ShowToastAsync("Помилка під час встановлення Л.І.А.").ConfigureAwait(true);
            }
            finally
            {
                BtnLiaInstall.IsEnabled = true;
                await UpdateLiaVersionAsync();
            }
        }

        private async void BtnLiaDelete_Click(object sender, RoutedEventArgs e)
        {
            BtnLiaInstall.IsEnabled = false;
            BtnLiaDelete.IsEnabled = false;
            TxtLiaSetupe.Text = string.Empty;

            try
            {
                Action<string> logCallback = msg =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtLiaSetupe.Text += $"{msg}\n";
                        TxtLiaSetupe.ScrollToEnd();
                    });
                };

                await _updater.UninstallAsync(logCallback).ConfigureAwait(true);
                await _toastService.ShowToastAsync("Л.І.А успішно видалено.").ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                TxtLiaVersionPath.Text = $"Помилка при видаленні: {ex.Message}";
                TxtLiaVersionPath.Foreground = MapColor(LiaStatusColor.Red);
            }
            finally
            {
                BtnLiaInstall.IsEnabled = true;
                await UpdateLiaVersionAsync();
            }
        }
        private async void BtnReset_Cash(object sender, RoutedEventArgs e)
        {
            await _cacheCleanupController.HandleManualCleanupAsync(CancellationToken.None).ConfigureAwait(true);
        }

        private async Task UpdateGameFolderUiAsync(string? folder)
        {
            if (!string.IsNullOrWhiteSpace(folder))
            {
                localFolder = folder;
                TxtSelectedPath.Text = folder;

                _buttonHelper.SetButtonState(BtnAutoSearch, true);
                _buttonStateManager.SetButtonEnabled(BtnSelectFolder, false);

                if (EnvSelector != null)
                    await EnvSelector.UpdateFromGameFolderAsync(localFolder).ConfigureAwait(true);

                BtnInstall.Content = _buttonHelper.GetInstallButtonText(EnvSelector?.SelectedEnvironment, localFolder);
            }
            else
            {
                localFolder = string.Empty;
                TxtSelectedPath.Text = DefaultPathText;

                _buttonHelper.SetButtonState(BtnAutoSearch, false);
                _buttonStateManager.SetButtonEnabled(BtnSelectFolder, true);

                if (EnvSelector != null)
                    await EnvSelector.UpdateFromGameFolderAsync(null).ConfigureAwait(true);
            }
        }

        private async Task ShowStartupToastsAsync()
        {
            if (_showGameFolderToast && !_viewModel.IsGameFolderSet && !string.IsNullOrWhiteSpace(MissingGameFolderToastText))
            {
                await _toastService.ShowToastAsync(MissingGameFolderToastText, 7000).ConfigureAwait(true);
            }

        }

        private async Task UpdateLiaVersionAsync()
        {
            var status = await _updater.GetStatusAsync().ConfigureAwait(true);

            // Зберігаємо статус для диспетчеризації кнопки без зайвого мережевого запиту.
            _lastLiaStatus = status;

            TxtLiaVersionPath.Text = status.Message;
            TxtLiaVersionPath.Foreground = MapColor(status.Color);

            BtnLiaInstall.Content = _buttonHelper.GetLiaInstallButtonText(status);
            BtnLiaDelete.IsEnabled = status.IsInstalled;
            BtnLiaInstall.IsEnabled = true;
        }

        private static Brush MapColor(LiaStatusColor color) => color switch
        {
            LiaStatusColor.Red => Brushes.Red,
            LiaStatusColor.Orange => Brushes.Orange,
            LiaStatusColor.Green => Brushes.LimeGreen,
            _ => Brushes.LightSlateGray
        };
    }
}
