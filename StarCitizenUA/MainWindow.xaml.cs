using StarCitizenUA.Controls;
using StarCitizenUA.Helpers;
using StarCitizenUA.Interfaces;
using StarCitizenUA.Services;
using StarCitizenUA.Services.Cache;
using StarCitizenUA.Services.LiaServices;
using StarCitizenUA.ViewModels;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        private bool _showGameFolderToast = true;
        private bool _showVoiceAttackFolderToast = true;
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
        private Button BtnLiaSettings => CanvasAssistant.OpenSettingsButton;
        private Button BtnSelectLiaFolder => CanvasLiaSettings.SelectFolderButton;
        private Button BtnLiaAutoSearch => CanvasLiaSettings.AutoSearchButton;
        private Button BtnLiaReturn => CanvasLiaSettings.ReturnButton;
        private Button BtnVaReturn => CanvasVoiceAttack.ReturnButton;
        private TextBox TxtLiaSelectedPath => CanvasLiaSettings.SelectedPathTextBox;
        internal RichTextBox TxtLiaReadme => CanvasAssistant.ReadmeTextBox;
        internal TextBox TxtLiaSettingsReadme => CanvasLiaSettings.ReadmeTextBox;
        internal TextBox TxtLiaSetupe => CanvasAssistant.SetupInfoTextBox;
        internal TextBox TxtLiaVersionPath => CanvasAssistant.TxtLiaVersionPath;
        private Button BtnLiaSettingsVA => CanvasAssistant.BtnLiaSettingsVA;
        internal TextBox TxtVoiceAttackReadme => CanvasVoiceAttack.ReadmeTextBox;
        private Button BtnLiaDelete => CanvasAssistant.BtnLiaDelete;

        private CancellationTokenSource? _voiceAttackSearchCts;
        private string? localFolder = string.Empty;
        private string? localLiaFolder = string.Empty;
        public string DefaultPathText = string.Empty;
        public string MissingGameFolderToastText = string.Empty;
        public string MissingVoiceAttackFolderToastText = string.Empty;
        private bool isSettingButtonClicked;
        private readonly UpdateCheckerService _updateCheckerService;
        private readonly CleanupController _cacheCleanupController;

        public MainWindow(MainWindowViewModel viewModel, IWindowHelper windowHelper, ILocalizationInstaller localizationInstaller, IReadmeService readmeService, IUpdater updater, UpdateCheckerService updateCheckerService)
        {
            InitializeComponent();

            _viewModel = viewModel;
            _windowHelper = windowHelper;
            _localizationInstaller = localizationInstaller;
            _readmeService = readmeService;
            _updater = updater;
            _updateCheckerService = updateCheckerService;

            _toastService = new ToastService(AppToast.ToastBorder, AppToast.ToastText);
            _linkService = new LinkService(_toastService);

            var options = new CacheCleanupOptions();
            var inspector = new ShaderCacheInspector(options);
            var cleaner = new CacheCleaner(options);
            _cacheCleanupController = new CleanupController(inspector, cleaner, _toastService, Dispatcher);

            _canvasManager = new CanvasManager(this);
            _buttonStateManager = new ButtonStateManager(BtnLocalization, BtnAssistant, BtnSettings, BtnSelectFolder, BtnSelectLiaFolder);
            _buttonHelper = new ButtonHelper();

            DataContext = _viewModel;
            DefaultPathText = TxtSelectedPath.Text;

            _buttonHelper.SetButtonState(BtnAutoSearch, _viewModel.IsGameFolderSet);
            _buttonHelper.SetButtonState(BtnLiaAutoSearch, _viewModel.IsVoiceAttackFolderSet);

            Loaded += MainWindow_Loaded;
            EnvSelector.GearClicked += EnvSelector_GearClicked;
            EnvSelector.SelectedEnvironmentChanged += (s, e) =>
            {
                BtnInstall.Content = _buttonHelper.GetInstallButtonText(EnvSelector?.SelectedEnvironment, _viewModel.GameFolder);
            };
            BtnAutoSearch.Loaded += (s, e) => _buttonHelper.SetButtonState(BtnAutoSearch, _viewModel.IsGameFolderSet);
            BtnLiaAutoSearch.Loaded += (s, e) => _buttonHelper.SetButtonState(BtnLiaAutoSearch, _viewModel.IsVoiceAttackFolderSet);

            BtnReturnLocalizationHome.Click += ReturnToHome_Click;
            BtnAssistantReturnHome.Click += ReturnToHome_Click;
            BtnSettingsReturn.Click += ReturnToLocalization_Click;
            BtnLiaReturn.Click += ReturnToAssistant_Click;
            BtnVaReturn.Click += ReturnToAssistant_Click;

            BtnInstall.Click += BtnInstall_Click;
            BtnLocalisationDelete.Click += LocalisationDelete_Click;
            BtnSelectFolder.Click += BtnSelectFolder_Click;
            BtnAutoSearch.Click += BtnAutoSearch_Click;
            BtnResetCash.Click += BtnReset_Cash;
            BtnLiaInstall.Click += BtnLiaInstall_Click;
            BtnLiaSettings.Click += LiaSettings_Click;
            BtnSelectLiaFolder.Click += BtnSelectLiaFolder_Click;
            BtnLiaAutoSearch.Click += BtnLiaAutoSearch_Click;
            BtnLiaSettingsVA.Click += BtnLiaSettingsVA_Click;
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
            BtnLiaAutoSearch.ApplyTemplate();
            BtnAutoSearch.IsEnabled = true;
            BtnLiaAutoSearch.IsEnabled = true;
            _canvasManager.ShowCanvas("home");
            var tasks = new Task[]
            {
                UpdateLiaVersionAsync(),
                _viewModel.InitializeAsync(),
                UpdateGameFolderUiAsync(_viewModel.GameFolder),
                UpdateLiaFolderUiAsync(_viewModel.VoiceAttackFolder)
            };

            await Task.WhenAll(tasks).ConfigureAwait(true);

            _ = ShowStartupToastsAsync();
            _ = _cacheCleanupController.RunStartupPromptAsync(CancellationToken.None);
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

        private void LiaSettings_Click(object sender, RoutedEventArgs e)
        {
            _canvasManager.SwitchCanvas(CanvasLiaSettings);
            _buttonStateManager.SetActive("assistant");
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
            _showVoiceAttackFolderToast = false;
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

        private async void BtnSelectLiaFolder_Click(object sender, EventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string selectedPath = dialog.SelectedPath;

                try
                {
                    var parts = selectedPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    int n = parts.Length;

                    if (n >= 3 &&
                        string.Equals(parts[n - 1], "Import", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(parts[n - 2], "Apps", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(parts[n - 3], "VoiceAttack 2", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_viewModel.TrySetVoiceAttackFolder(selectedPath))
                        {
                            await UpdateLiaFolderUiAsync(_viewModel.VoiceAttackFolder).ConfigureAwait(true);
                            await _toastService.ShowToastAsync($"Вибрано папку: {_viewModel.VoiceAttackFolder}").ConfigureAwait(true);
                            BtnLiaInstall.IsEnabled = true;
                        }
                        return;
                    }

                    TxtLiaVersionPath.Text = "❌ Вибрана директорія повинна бути VoiceAttack 2\\Apps\\Import";
                    TxtLiaVersionPath.Foreground = System.Windows.Media.Brushes.Red;
                    BtnLiaInstall.IsEnabled = false;
                    await _toastService.ShowToastAsync("Оберіть правильну папку VoiceAttack.").ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    TxtLiaVersionPath.Text = $"❌ Помилка при перевірці шляху: {ex.Message}";
                    TxtLiaVersionPath.Foreground = System.Windows.Media.Brushes.Red;
                    BtnLiaInstall.IsEnabled = false;
                }
            }
        }

        private async void BtnLiaAutoSearch_Click(object sender, RoutedEventArgs e)
        {
            _showVoiceAttackFolderToast = false;
            BtnLiaAutoSearch.ApplyTemplate();

            if (!_viewModel.IsVoiceAttackFolderSet)
            {
                _voiceAttackSearchCts?.Cancel();
                _voiceAttackSearchCts = new CancellationTokenSource();

                try
                {
                    var foundFolder = await _viewModel.DetectVoiceAttackFolderAsync(_voiceAttackSearchCts.Token);
                    if (!string.IsNullOrEmpty(foundFolder))
                    {
                        await UpdateLiaFolderUiAsync(foundFolder).ConfigureAwait(true);
                        await _toastService.ShowToastAsync($"Знайдено папку: {foundFolder}").ConfigureAwait(true);
                    }
                    else
                    {
                        await _toastService.ShowToastAsync("Не вдалося знайти папку. Будь ласка, оберіть вручну.").ConfigureAwait(true);
                    }
                }
                catch (OperationCanceledException)
                {
                    await _toastService.ShowToastAsync("Пошук LIA перервано.", 3000).ConfigureAwait(true);
                }
            }
            else
            {
                _viewModel.ResetVoiceAttackFolder();
                await UpdateLiaFolderUiAsync(null).ConfigureAwait(true);
                await _toastService.ShowToastAsync("Збережений шлях успішно скинуто.").ConfigureAwait(true);
            }
        }

        private async void BtnLiaInstall_Click(object sender, RoutedEventArgs e)
        {
            TxtLiaSetupe.Text = string.Empty;
            BtnLiaInstall.IsEnabled = false;

            try
            {
                Action<string> logCallback = msg =>
                {
                    Dispatcher.Invoke(() => TxtLiaSetupe.Text += $"{msg}\n");
                    TxtLiaSetupe.ScrollToEnd();
                };

                var remoteFiles = await _updater.GetRemoteFileListAsync();
                await _updater.SyncFilesAsync(remoteFiles, localLiaFolder!, logCallback);
                await _updater.DownloadAndInstallVoskModelAsync(localLiaFolder!, logCallback);

                var (updateMessage, _) = await _updateCheckerService.CheckForPendingUpdatesAsync();

                BtnLiaInstall.Content = _buttonHelper.GetLiaInstallButtonText(updateMessage);

                await _toastService.ShowToastAsync("Вітаємо! У вас актуальна версія голосового асистента Л.І.А!");
                await UpdateLiaVersionAsync();
            }
            catch (Exception ex)
            {
                TxtLiaSetupe.Text += $"\n❌ Помилка: {ex.Message}";
                await _toastService.ShowToastAsync("Помилка під час встановлення.");
            }
            finally
            {
                BtnLiaInstall.IsEnabled = true;
            }
        }

        private async void BtnLiaDelete_Click(object sender, RoutedEventArgs e)
        {
            BtnLiaInstall.IsEnabled = false;

            try
            {
                if (string.IsNullOrWhiteSpace(localLiaFolder) || !Directory.Exists(localLiaFolder))
                {
                    TxtLiaVersionPath.Text = "Папка VoiceAttack не обрана або не існує.";
                    TxtLiaVersionPath.Foreground = System.Windows.Media.Brushes.Red;
                    BtnLiaInstall.IsEnabled = true;
                    return;
                }

                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string versionFilePath = Path.Combine(localAppData, "L.I.A Voice Pack Updater", "version.txt");

                List<string> filesToDelete = new();

                if (File.Exists(versionFilePath))
                {
                    var lines = await File.ReadAllLinesAsync(versionFilePath);
                    int startIndex = Array.FindIndex(lines, l => l.StartsWith("minFiles=", StringComparison.OrdinalIgnoreCase)) + 1;
                    for (int i = startIndex; i < lines.Length; i++)
                    {
                        string line = lines[i].Trim();
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        string filePath = line.Split('|')[0].Trim().Replace('/', Path.DirectorySeparatorChar);
                        filesToDelete.Add(filePath);
                    }
                }

                filesToDelete.Add("Star Citizen LIA-Profile.vap");

                foreach (var file in filesToDelete)
                {
                    string fullPath = Path.Combine(localLiaFolder, file);
                    if (File.Exists(fullPath))
                    {
                        File.SetAttributes(fullPath, FileAttributes.Normal);
                        File.Delete(fullPath);
                    }
                }

                string[] foldersToDelete = { "StarCitizenKeyBinding", "model-ua", "INFO" };
                foreach (var folderName in foldersToDelete)
                {
                    string folderPath = Path.Combine(localLiaFolder, folderName);
                    if (Directory.Exists(folderPath))
                    {
                        // Скидаємо атрибути файлів та папок всередині
                        foreach (var file in Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories))
                            File.SetAttributes(file, FileAttributes.Normal);

                        foreach (var dir in Directory.GetDirectories(folderPath, "*", SearchOption.AllDirectories))
                            new DirectoryInfo(dir).Attributes = FileAttributes.Normal;

                        new DirectoryInfo(folderPath).Attributes = FileAttributes.Normal;
                        Directory.Delete(folderPath, true);
                    }
                }

                string updaterFolder = Path.Combine(localAppData, "L.I.A Voice Pack Updater");
                if (Directory.Exists(updaterFolder))
                {
                    foreach (var file in Directory.GetFiles(updaterFolder, "*", SearchOption.AllDirectories))
                        File.SetAttributes(file, FileAttributes.Normal);

                    foreach (var dir in Directory.GetDirectories(updaterFolder, "*", SearchOption.AllDirectories))
                        new DirectoryInfo(dir).Attributes = FileAttributes.Normal;

                    new DirectoryInfo(updaterFolder).Attributes = FileAttributes.Normal;
                    Directory.Delete(updaterFolder, true);
                }

                TxtLiaVersionPath.Foreground = System.Windows.Media.Brushes.Orange;
                BtnLiaInstall.Content = "Встановити";
                await UpdateLiaVersionAsync();
                await _toastService.ShowToastAsync("Файли голосового асистента успішно видалені.");
            }
            catch (Exception ex)
            {
                TxtLiaVersionPath.Text = $"Помилка при видаленні: {ex.Message}";
                TxtLiaVersionPath.Foreground = System.Windows.Media.Brushes.Red;
                await UpdateLiaVersionAsync();
            }
        }

        private async void BtnReset_Cash(object sender, RoutedEventArgs e)
        {
            await _cacheCleanupController.HandleManualCleanupAsync(CancellationToken.None).ConfigureAwait(true);
        }

        private void BtnLiaSettingsVA_Click(object sender, RoutedEventArgs e)
        {
            _canvasManager.SwitchCanvas(CanvasVoiceAttack);
            _buttonStateManager.SetActive("voiceattack");
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

        private async Task UpdateLiaFolderUiAsync(string? folder)
        {
            if (!string.IsNullOrWhiteSpace(folder))
            {
                localLiaFolder = folder;
                TxtLiaSelectedPath.Text = folder;

                _buttonHelper.SetButtonState(BtnLiaAutoSearch, true);
                _buttonStateManager.SetButtonEnabled(BtnSelectLiaFolder, false);
                BtnLiaInstall.IsEnabled = true;

                await UpdateLiaVersionAsync();
            }
            else
            {
                localLiaFolder = string.Empty;
                TxtLiaSelectedPath.Text = DefaultPathText;

                _buttonHelper.SetButtonState(BtnLiaAutoSearch, false);
                _buttonStateManager.SetButtonEnabled(BtnSelectLiaFolder, true);
                BtnLiaInstall.IsEnabled = false;

                Dispatcher.Invoke(() => TxtLiaVersionPath.Text = "Папка VoiceAttack не обрана");
                TxtLiaVersionPath.Foreground = System.Windows.Media.Brushes.Red;
            }

            BtnLiaInstall.Content = _buttonHelper.GetLiaInstallButtonText(TxtLiaVersionPath.Text);
        }

        private async Task ShowStartupToastsAsync()
        {
            if (_showGameFolderToast && !_viewModel.IsGameFolderSet && !string.IsNullOrWhiteSpace(MissingGameFolderToastText))
            {
                await _toastService.ShowToastAsync(MissingGameFolderToastText, 7000).ConfigureAwait(true);
            }

            if (_showVoiceAttackFolderToast && !_viewModel.IsVoiceAttackFolderSet && !string.IsNullOrWhiteSpace(MissingVoiceAttackFolderToastText))
            {
                await _toastService.ShowToastAsync(MissingVoiceAttackFolderToastText, 7000).ConfigureAwait(true);
            }
        }

        private async Task UpdateLiaVersionAsync()
        {
            if (!string.IsNullOrWhiteSpace(localLiaFolder) && Directory.Exists(localLiaFolder))
            {
                var (message, color) = await _updateCheckerService.CheckForPendingUpdatesAsync();
                if (message == "Голосовий пакет не встановлено.")
                {
                    BtnLiaDelete.IsEnabled = false;
                }
                else
                {
                    BtnLiaDelete.IsEnabled = true;
                }
                Dispatcher.Invoke(() =>
                {
                    TxtLiaVersionPath.Text = message;
                    TxtLiaVersionPath.Foreground = color;
                    BtnLiaInstall.Content = _buttonHelper.GetLiaInstallButtonText(message);
                });
            }
            else
            {
                Dispatcher.Invoke(() => TxtLiaVersionPath.Text = "Папка VoiceAttack не обрана");
                BtnLiaInstall.Content = "Встановити";
                BtnLiaDelete.IsEnabled = false;
                BtnLiaInstall.IsEnabled = false;
            }
        }
    }
}