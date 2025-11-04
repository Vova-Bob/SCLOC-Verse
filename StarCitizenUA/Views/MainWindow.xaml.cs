using StarCitizenUA.Controls;
using StarCitizenUA.Helpers;
using StarCitizenUA.Interfaces;
using StarCitizenUA.Services;
using StarCitizenUA.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;

namespace StarCitizenUA.Views
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
        private TextBox TxtLiaSelectedPath => CanvasLiaSettings.SelectedPathTextBox;
        internal TextBox TxtLiaReadme => CanvasAssistant.ReadmeTextBox;
        internal TextBox TxtLiaSettingsReadme => CanvasLiaSettings.ReadmeTextBox;



        private CancellationTokenSource? _voiceAttackSearchCts;

        private string? localFolder = string.Empty;
        private string? localLiaFolder = string.Empty;
        public string DefaultPathText = string.Empty;
        public string MissingGameFolderToastText = string.Empty;
        public string MissingVoiceAttackFolderToastText = string.Empty;
        private bool isSettingButtonClicked;

        public MainWindow(MainWindowViewModel viewModel, IWindowHelper windowHelper, ILocalizationInstaller localizationInstaller, IReadmeService readmeService)
        {
            InitializeComponent();

            _viewModel = viewModel;
            _windowHelper = windowHelper;
            _localizationInstaller = localizationInstaller;
            _readmeService = readmeService;

            _toastService = new ToastService(AppToast.ToastBorder, AppToast.ToastText);
            _linkService = new LinkService(_toastService);

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

            BtnInstall.Click += BtnInstall_Click;
            BtnLocalisationDelete.Click += LocalisationDelete_Click;
            BtnSelectFolder.Click += BtnSelectFolder_Click;
            BtnAutoSearch.Click += BtnAutoSearch_Click;
            BtnResetCash.Click += BtnReset_Cash;
            BtnLiaInstall.Click += BtnLiaInstall_Click;
            BtnLiaSettings.Click += LiaSettings_Click;
            BtnSelectLiaFolder.Click += BtnSelectLiaFolder_Click;
            BtnLiaAutoSearch.Click += BtnLiaAutoSearch_Click;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _windowHelper.ApplyWindowRoundCorners(this);
            MainGrid.MouseMove += (s, e2) => _windowHelper.HandleMouseMove(this, bgImage, e2.GetPosition(MainGrid), MainGrid);
            MainGrid.MouseLeave += (s, e2) => _windowHelper.HandleMouseLeave(this, bgImage, MainGrid);
            _readmeService.LoadReadme(this);

            BtnAutoSearch.ApplyTemplate();
            BtnLiaAutoSearch.ApplyTemplate();

            _canvasManager.ShowCanvas("home");

            await _viewModel.InitializeAsync().ConfigureAwait(true);

            await UpdateGameFolderUiAsync(_viewModel.GameFolder).ConfigureAwait(true);
            await UpdateLiaFolderUiAsync(_viewModel.VoiceAttackFolder).ConfigureAwait(true);

            await ShowStartupToastsAsync().ConfigureAwait(true);
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

        private void Assistant_Click(object sender, RoutedEventArgs e)
        {
            _canvasManager.SwitchCanvas(CanvasAssistant);
            _buttonStateManager.SetActive("assistant");
            isSettingButtonClicked = false;
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

        private async void BtnSelectLiaFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (_viewModel.TrySetVoiceAttackFolder(dialog.SelectedPath))
                {
                    await UpdateLiaFolderUiAsync(_viewModel.VoiceAttackFolder).ConfigureAwait(true);
                    await _toastService.ShowToastAsync($"Вибрано папку: {_viewModel.VoiceAttackFolder}").ConfigureAwait(true);
                }
                else
                {
                    await _toastService.ShowToastAsync("Обраний шлях не існує. Спробуйте інший.").ConfigureAwait(true);
                }
            }
        }

        private async void BtnLiaAutoSearch_Click(object sender, RoutedEventArgs e)
        {
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
            await _toastService.ShowToastAsync("Функція встановлення LIA ще не реалізована.").ConfigureAwait(true);
        }

        private async void BtnReset_Cash(object sender, RoutedEventArgs e)
        {
            await _toastService.ShowToastAsync("Кеш очищено.").ConfigureAwait(true);
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

        private Task UpdateLiaFolderUiAsync(string? folder)
        {
            if (!string.IsNullOrWhiteSpace(folder))
            {
                localLiaFolder = folder;
                TxtLiaSelectedPath.Text = folder;

                _buttonHelper.SetButtonState(BtnLiaAutoSearch, true);
                _buttonStateManager.SetButtonEnabled(BtnSelectLiaFolder, false);
            }
            else
            {
                localLiaFolder = string.Empty;
                TxtLiaSelectedPath.Text = DefaultPathText;

                _buttonHelper.SetButtonState(BtnLiaAutoSearch, false);
                _buttonStateManager.SetButtonEnabled(BtnSelectLiaFolder, true);
            }

            return Task.CompletedTask;
        }

        private async Task ShowStartupToastsAsync()
        {
            if (!_viewModel.IsGameFolderSet && !string.IsNullOrWhiteSpace(MissingGameFolderToastText))
            {
                await _toastService.ShowToastAsync(MissingGameFolderToastText, 7000).ConfigureAwait(true);
            }

            if (!_viewModel.IsVoiceAttackFolderSet && !string.IsNullOrWhiteSpace(MissingVoiceAttackFolderToastText))
            {
                await _toastService.ShowToastAsync(MissingVoiceAttackFolderToastText, 7000).ConfigureAwait(true);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _voiceAttackSearchCts?.Cancel();
            _voiceAttackSearchCts?.Dispose();
            _viewModel.CancelVoiceAttackSearch();
            base.OnClosed(e);
        }
    }
}
