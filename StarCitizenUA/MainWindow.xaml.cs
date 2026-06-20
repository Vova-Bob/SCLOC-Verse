using StarCitizenUA.Controls;
using StarCitizenUA.Helpers;
using StarCitizenUA.Interfaces;
using StarCitizenUA.Models.ApplicationUpdate;
using StarCitizenUA.Models.LiaModels;
using StarCitizenUA.Services;
using StarCitizenUA.Services.ApplicationUpdate;
using StarCitizenUA.Services.Cache;
using StarCitizenUA.Windows;
using StarCitizenUA.Services.LiaServices;
using StarCitizenUA.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;

namespace StarCitizenUA
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
        private readonly IReleaseChannelResolver _releaseChannelResolver;
        private readonly UpdateStatusPresenter _updateStatusPresenter;
        private bool _showGameFolderToast = true;
        private DateTime? _suppressStartupUpdateCheckUntil;
        private EnvironmentSelector EnvSelector => CanvasLocalization.EnvironmentSelector;
        private Button BtnInstall => CanvasLocalization.InstallButton;
        private Button BtnLocalisationDelete => CanvasLocalization.DeleteButton;
        internal TextBox TxtLocalizationReadme => CanvasLocalization.ReadmeTextBox;
        private Button BtnReturnLocalizationHome => CanvasLocalization.ReturnHomeButton;
        private Button BtnAssistantReturnHome => CanvasAssistant.ReturnHomeButton;
        private Button BtnSelectFolder => CanvasSettings.SelectFolderButton;
        private Button BtnAutoSearch => CanvasSettings.AutoSearchButton;
        private Button BtnResetCash => CanvasSettings.ResetCacheButton;
        private Button BtnSettingsReturn => CanvasSettings.ReturnButton;
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

        public MainWindow(MainWindowViewModel viewModel, IWindowHelper windowHelper, ILocalizationInstaller localizationInstaller, IReadmeService readmeService,     IUpdater updater, UpdateCheckerService updateCheckerService, IApplicationUpdateService applicationUpdateService, IBackgroundUpdateMonitor backgroundUpdateMonitor, IUpdateChannelService updateChannelService, IApplicationVersionProvider applicationVersionProvider, IUpdateDownloader updateDownloader, IUpdateInstaller updateInstaller, IUpdateHistoryService updateHistoryService, IUpdateVerifier updateVerifier, IGitHubReleaseClient gitHubReleaseClient, IReleaseChannelResolver releaseChannelResolver)
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
            _releaseChannelResolver = releaseChannelResolver;

            _toastService = new ToastService(AppToast.ToastBorder, AppToast.ToastText);
            _linkService = new LinkService(_toastService);
            _updateStatusPresenter = new UpdateStatusPresenter(
                CanvasHome.CurrentVersionTextControl,
                CanvasHome.AvailableVersionTextControl,
                CanvasHome.UpdateStatusTextControl,
                CanvasHome.UpdateCheckButtonControl,
                CanvasHome.UpdatePanel,
                CanvasHome.HideUpdatePanelStoryboard);

            var options = new CacheCleanupOptions();
            var inspector = new ShaderCacheInspector(options);
            var cleaner = new CacheCleaner(options);
            _cacheCleanupController = new CleanupController(inspector, cleaner, _toastService, Dispatcher);

            _canvasManager = new CanvasManager(this);
            _buttonStateManager = new ButtonStateManager(BtnLocalization, BtnAssistant, BtnSettings, BtnSelectFolder);
            _buttonHelper = new ButtonHelper();

            DataContext = _viewModel;
            DefaultPathText = TxtSelectedPath.Text;

            _buttonHelper.SetButtonState(BtnAutoSearch, _viewModel.IsGameFolderSet);

            CanvasHome.UpdateCheckButtonControl.Click += CheckUpdateButton_Click;
            CanvasHome.CurrentVersionTextControl.Text = _applicationVersionProvider.GetCurrentVersion().ToString();

            _backgroundUpdateMonitor.UpdateAvailable += OnBackgroundUpdateAvailable;
            _backgroundUpdateMonitor.CheckFailed += OnBackgroundUpdateCheckFailed;

            CanvasSettings.UpdateChannelSelector.ItemsSource = new[]
            {
                new { DisplayName = "Стабільний", Value = UpdateChannel.Stable },
                new { DisplayName = "Тестовий", Value = UpdateChannel.Dev }
            };
            CanvasSettings.UpdateChannelSelector.SelectionChanged += UpdateChannelSelector_SelectionChanged;
            CanvasSettings.UpdateHistoryButtonControl.Click += UpdateHistoryButton_Click;

            Loaded += MainWindow_Loaded;
            EnvSelector.GearClicked += EnvSelector_GearClicked;
            EnvSelector.SelectedEnvironmentChanged += (s, e) =>
            {
                BtnInstall.Content = _buttonHelper.GetInstallButtonText(EnvSelector?.SelectedEnvironment, _viewModel.GameFolder);
            };
            BtnAutoSearch.Loaded += (s, e) => _buttonHelper.SetButtonState(BtnAutoSearch, _viewModel.IsGameFolderSet);

            BtnReturnLocalizationHome.Click += ReturnToHome_Click;
            BtnAssistantReturnHome.Click += ReturnToHome_Click;
            BtnSettingsReturn.Click += ReturnToLocalization_Click;

            BtnInstall.Click += BtnInstall_Click;
            BtnLocalisationDelete.Click += LocalisationDelete_Click;
            BtnSelectFolder.Click += BtnSelectFolder_Click;
            BtnAutoSearch.Click += BtnAutoSearch_Click;
            BtnResetCash.Click += BtnReset_Cash;
            BtnLiaInstall.Click += BtnLiaInstall_Click;
            BtnLiaDelete.Click += BtnLiaDelete_Click;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _windowHelper.ApplyWindowRoundCorners(this);
            MainGrid.MouseMove += (s, e2) => _windowHelper.HandleMouseMove(this, bgImage, e2.GetPosition(MainGrid), MainGrid);
            MainGrid.MouseLeave += (s, e2) => _windowHelper.HandleMouseLeave(this, bgImage, MainGrid);
            _readmeService.LoadReadme(this);
            CanvasHome.ToastService = _toastService;
            CanvasHome.LinkService = _linkService;
            BtnAutoSearch.ApplyTemplate();
            BtnAutoSearch.IsEnabled = true;
            _canvasManager.ShowCanvas("home");
            var tasks = new Task[]
            {
                UpdateLiaVersionAsync(),
                _viewModel.InitializeAsync(),
                UpdateGameFolderUiAsync(_viewModel.GameFolder)
            };

            await Task.WhenAll(tasks).ConfigureAwait(true);

            InitializeUpdateChannel();

            _ = ShowStartupToastsAsync();
            _ = _cacheCleanupController.RunStartupPromptAsync(CancellationToken.None);

            _ = RunStartupUpdateCheckAsync();
        }

        private void InitializeUpdateChannel()
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

        private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            await RunManualUpdateCheckAsync(forceRefresh: true).ConfigureAwait(true);
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
            finally
            {
                _updateStatusPresenter.EnableActionButton();
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

                    var dialogResult = MessageBox.Show(
                        $"Доступна нова версія {result.LatestVersion}. Бажаєте завантажити та встановити?",
                        "Оновлення програми",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (dialogResult == MessageBoxResult.Yes)
                    {
                        await InstallUpdateAsync(result).ConfigureAwait(true);
                    }
                    else
                    {
                        await _updateHistoryService.AddEntryAsync(CreateHistoryEntry(UpdateOperation.Install, UpdateOperationResult.Cancelled, result, "Користувач відмовився від встановлення.")).ConfigureAwait(true);
                    }
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

        private async Task RunStartupUpdateCheckAsync()
        {
            await Task.Delay(UpdateConstants.StartupUpdateCheckDelay).ConfigureAwait(true);

            if (_suppressStartupUpdateCheckUntil.HasValue && DateTime.Now < _suppressStartupUpdateCheckUntil.Value)
            {
                _backgroundUpdateMonitor.Start();
                return;
            }

            await RunManualUpdateCheckAsync(forceRefresh: false).ConfigureAwait(true);
            _backgroundUpdateMonitor.Start();
        }

        private async void OnBackgroundUpdateAvailable(object? sender, UpdateCheckResult result)
        {
            await _toastService.ShowToastAsync($"Доступна нова версія SCLOC-Verse {result.LatestVersion}").ConfigureAwait(true);
        }

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

            string installerPath;
            try
            {
                installerPath = await _updateDownloader.DownloadAsync(
                    result.DownloadUrl,
                    updateDirectory,
                    CancellationToken.None).ConfigureAwait(true);

                await _updateHistoryService.AddEntryAsync(CreateHistoryEntry(UpdateOperation.Download, UpdateOperationResult.Success, result)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                await _updateHistoryService.AddEntryAsync(CreateHistoryEntry(UpdateOperation.Download, UpdateOperationResult.Failed, result, ex.Message)).ConfigureAwait(true);
                CanvasHome.UpdateStatusTextControl.Text = "Помилка завантаження";
                CanvasHome.UpdateStatusTextControl.Foreground = Brushes.Red;
                await _toastService.ShowToastAsync($"Помилка завантаження: {ex.Message}").ConfigureAwait(true);
                return;
            }

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
                        CancellationToken.None).ConfigureAwait(true);

                    if (!isValid)
                    {
                        await _updateHistoryService.AddEntryAsync(CreateHistoryEntry(UpdateOperation.Verify, UpdateOperationResult.Failed, result, "Невідповідність контрольної суми.")).ConfigureAwait(true);
                        CanvasHome.UpdateStatusTextControl.Text = "Помилка перевірки файлу";
                        CanvasHome.UpdateStatusTextControl.Foreground = Brushes.Red;
                        await _toastService.ShowToastAsync("Помилка перевірки файлу оновлення.").ConfigureAwait(true);
                        return;
                    }

                    await _updateHistoryService.AddEntryAsync(CreateHistoryEntry(UpdateOperation.Verify, UpdateOperationResult.Success, result)).ConfigureAwait(true);
                }
            }
            catch (Exception ex)
            {
                await _updateHistoryService.AddEntryAsync(CreateHistoryEntry(UpdateOperation.Verify, UpdateOperationResult.Failed, result, ex.Message)).ConfigureAwait(true);
                CanvasHome.UpdateStatusTextControl.Text = "Помилка перевірки файлу";
                CanvasHome.UpdateStatusTextControl.Foreground = Brushes.Red;
                await _toastService.ShowToastAsync($"Помилка перевірки файлу: {ex.Message}").ConfigureAwait(true);
                return;
            }

            try
            {
                var currentExePath = Environment.ProcessPath
                    ?? throw new InvalidOperationException("Unable to determine executable path.");

                var installStarted = await _updateInstaller.InstallAsync(
                    installerPath,
                    currentExePath,
                    CancellationToken.None).ConfigureAwait(true);

                if (installStarted)
                {
                    await _updateHistoryService.AddEntryAsync(CreateHistoryEntry(UpdateOperation.Install, UpdateOperationResult.Success, result)).ConfigureAwait(true);
                    await _toastService.ShowToastAsync("Оновлення встановлюється. Додаток буде перезапущено.", 2000).ConfigureAwait(true);
                    Application.Current.Shutdown();
                }
                else
                {
                    await _updateHistoryService.AddEntryAsync(CreateHistoryEntry(UpdateOperation.Install, UpdateOperationResult.Failed, result, "Не вдалося запустити процес встановлення.")).ConfigureAwait(true);
                    await _toastService.ShowToastAsync("Не вдалося запустити встановлення оновлення.").ConfigureAwait(true);
                }
            }
            catch (Exception ex)
            {
                await _updateHistoryService.AddEntryAsync(CreateHistoryEntry(UpdateOperation.Install, UpdateOperationResult.Failed, result, ex.Message)).ConfigureAwait(true);
                CanvasHome.UpdateStatusTextControl.Text = "Помилка встановлення";
                CanvasHome.UpdateStatusTextControl.Foreground = Brushes.Red;
                await _toastService.ShowToastAsync($"Помилка встановлення: {ex.Message}").ConfigureAwait(true);
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
            dynamic selected = CanvasSettings.UpdateChannelSelector.SelectedItem;
            if (selected != null)
            {
                _updateChannelService.SetUpdateChannel(selected.Value.ToString());
            }
        }

        private void UpdateHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new UpdateHistoryWindow(
                _gitHubReleaseClient,
                _applicationVersionProvider,
                _updateChannelService,
                _releaseChannelResolver,
                _linkService,
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
                    Channel = _releaseChannelResolver.ResolveChannel(release),
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
        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Localization_Click(object sender, RoutedEventArgs e)
        {
            _canvasManager.SwitchCanvas(CanvasLocalization);
            _buttonStateManager.SetActive("localization");
            isSettingButtonClicked = false;
        }

        private async void Assistant_Click(object sender, RoutedEventArgs e)
        {
            _canvasManager.SwitchCanvas(CanvasAssistant);
            _buttonStateManager.SetActive("assistant");
            isSettingButtonClicked = false;

            await UpdateLiaVersionAsync();
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            _canvasManager.SwitchCanvas(CanvasSettings);
            _buttonStateManager.SetActive("settings");
            isSettingButtonClicked = true;
        }

        private void LocalisationSettings_Click(object sender, RoutedEventArgs e)
        {
            _canvasManager.SwitchCanvas(CanvasSettings);
            _buttonStateManager.SetActive("settings");
        }

        private void ReturnToHome_Click(object sender, RoutedEventArgs e)
        {
            _canvasManager.SwitchCanvas(CanvasHome);
            _buttonStateManager.SetActive("home");
            isSettingButtonClicked = false;
        }

        private void ReturnToLocalization_Click(object sender, RoutedEventArgs e)
        {
            if (isSettingButtonClicked)
            {
                _canvasManager.SwitchCanvas(CanvasHome);
                _buttonStateManager.SetActive("home");
                isSettingButtonClicked = false;
            }
            else
            {
                _canvasManager.SwitchCanvas(CanvasLocalization);
                _buttonStateManager.SetActive("localization");
            }
        }

        private void ReturnToAssistant_Click(object sender, RoutedEventArgs e)
        {
            _canvasManager.SwitchCanvas(CanvasAssistant);
            _buttonStateManager.SetActive("assistant");
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
            TxtLiaSetupe.Text = string.Empty;
            BtnLiaInstall.IsEnabled = false;
            BtnLiaDelete.IsEnabled = false;

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
                await _toastService.ShowToastAsync("Л.І.А успішно встановлено або оновлено.").ConfigureAwait(true);
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

            TxtLiaVersionPath.Text = status.Message;
            TxtLiaVersionPath.Foreground = MapColor(status.Color);

            BtnLiaInstall.Content = _buttonHelper.GetLiaInstallButtonText(status.Message);
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
