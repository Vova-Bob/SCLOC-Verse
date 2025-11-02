using StarCitizenUA.Helpers;
using StarCitizenUA.Interfaces;
using StarCitizenUA.Services;
using StarCitizenUA.Services.LiaServices;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;

namespace StarCitizenUA.Views
{
    public partial class MainWindow : Window
    {
        private IWindowHelper _windowHelper;
        private ILinkService _linkService;
        private IToastService _toastService;
        private ISearchFolder _searchFolder;
        private IVoiceAttackFolderHelper _voiceAttackFolderHelper;
        private ICanvasManager _canvasManager;
        private IButtonStateManager _buttonStateManager;
        private ILocalizationInstaller _localizationInstaller;
        private readonly IReadmeService _readmeService;
        private IButtonHelper _buttonHelper;

        private string? localFolder = "";
        private string? localLiaFolder = "";
        public string DefaultPathText = "";
        private bool isPathSet = false;
        private bool isLiaPathSet = false;
        private bool isSettingButtonClicked = false;

        public MainWindow()
        {
            InitializeComponent();
            _toastService = new ToastService(ToastMessage, ToastText);
            _searchFolder = new SearchFolder(_toastService);
            _windowHelper = new WindowHelper();
            _linkService = new LinkService();
            _localizationInstaller = new LocalizationInstaller();
            _voiceAttackFolderHelper = new VoiceAttackFolderHelper(_toastService);
            _canvasManager = new CanvasManager(this);
            _buttonStateManager = new ButtonStateManager(BtnLocalization, BtnAssistant, BtnSettings, BtnSelectFolder, BtnSelectLiaFolder);
            _readmeService = new ReadmeService();
            _buttonHelper = new ButtonHelper();
            _buttonHelper.SetButtonState(BtnAutoSearch, isPathSet);
            _buttonHelper.SetButtonState(BtnLiaAutoSearch, isLiaPathSet);
            Loaded += MainWindow_Loaded;
            EnvSelector.GearClicked += EnvSelector_GearClicked;
            BtnAutoSearch.Loaded += (s, e) => _buttonHelper.SetButtonState(BtnAutoSearch, isPathSet);
            BtnLiaAutoSearch.Loaded += (s, e) => _buttonHelper.SetButtonState(BtnLiaAutoSearch, isLiaPathSet);


            EnvSelector.SelectedEnvironmentChanged += (s, e) =>
            {
                BtnInstall.Content = _buttonHelper.GetInstallButtonText(EnvSelector?.SelectedEnvironment, localFolder);
            };
        }

        private void EnvSelector_SelectionChanged(object? sender, EventArgs e)
        {
            _buttonHelper.GetInstallButtonText(EnvSelector?.SelectedEnvironment, localFolder);
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _windowHelper.ApplyWindowRoundCorners(this);
            MainGrid.MouseMove += (s, e2) => _windowHelper.HandleMouseMove(this, bgImage, e2.GetPosition(MainGrid), MainGrid);
            MainGrid.MouseLeave += (s, e2) => _windowHelper.HandleMouseLeave(this, bgImage, MainGrid);
            _readmeService.LoadReadme(this);

            BtnAutoSearch.ApplyTemplate();
            BtnLiaAutoSearch.ApplyTemplate();

            string savedPath = Settings.Default.StarCitizenUA;
            string savedLiaPath = Settings.Default.StarCitizenLIA;

            if (!string.IsNullOrEmpty(savedPath))
            {
                localFolder = savedPath;
                isPathSet = true;
                TxtSelectedPath.Text = savedPath;

                _buttonHelper.SetButtonState(BtnAutoSearch, true);
                _buttonStateManager.SetButtonEnabled(BtnSelectFolder, false);

                // ► підвантажуємо реальні папки/версії у селектор
                if (EnvSelector != null)
                    await EnvSelector.UpdateFromGameFolderAsync(localFolder);

                // оновлюємо текст кнопки встановлення
                BtnInstall.Content = _buttonHelper.GetInstallButtonText(EnvSelector?.SelectedEnvironment, localFolder);
            }
            else
            {
                localFolder = string.Empty;
                isPathSet = false;
                TxtSelectedPath.Text = DefaultPathText;

                _buttonHelper.SetButtonState(BtnAutoSearch, false);
                _buttonStateManager.SetButtonEnabled(BtnSelectLiaFolder, true);
            }

            if (!string.IsNullOrEmpty(savedLiaPath))
            {
                localLiaFolder = savedLiaPath;
                isLiaPathSet = true;
                TxtLiaSelectedPath.Text = savedLiaPath;

                _buttonHelper.SetButtonState(BtnLiaAutoSearch, true);
                _buttonStateManager.SetButtonEnabled(BtnSelectFolder, false);
            }
            else
            {
                localLiaFolder = string.Empty;
                isLiaPathSet = false;
                TxtLiaSelectedPath.Text = DefaultPathText;

                _buttonHelper.SetButtonState(BtnLiaAutoSearch, false);
                _buttonStateManager.SetButtonEnabled(BtnSelectLiaFolder, true);
            }

            _canvasManager.ShowCanvas("home");
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _windowHelper.DragWindow(this, e);
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                _linkService.OpenLink(e.Uri.AbsoluteUri);
                _toastService.ShowToast("Відкриваємо посилання у браузері.");
            }
            catch (Exception ex)
            {
                _toastService.ShowToast(ex.Message);
            }
            e.Handled = true;
        }

        //private void UpdateSelectButtonState(bool enabled)
        //{
        //    BtnSelectFolder.IsEnabled = enabled; // вимкнути/увімкнути кнопку
        //    BtnSelectFolder.Opacity = enabled ? 1.0 : 0.5; // затемнити якщо неактивна
        //}
        //private void UpdateLiaSelectButtonState(bool enabled)
        //{
        //    BtnSelectLiaFolder.IsEnabled = enabled; // вимкнути/увімкнути кнопку
        //    BtnSelectLiaFolder.Opacity = enabled ? 1.0 : 0.5; // затемнити якщо неактивна
        //}       

        private bool TryGetSelectedEnvironmentFolder(out string environmentFolder, out string environmentName, string operationDescription)
        {
            environmentFolder = string.Empty;
            environmentName = string.Empty;

            var selectedEnvironment = EnvSelector?.SelectedEnvironment;
            if (selectedEnvironment is null)
            {
                _toastService.ShowToast($"Оберіть середовище (LIVE, PTU тощо) для {operationDescription}.");
                return false;
            }

            environmentName = selectedEnvironment.Name;

            if (!string.IsNullOrWhiteSpace(selectedEnvironment.FolderPath))
            {
                environmentFolder = selectedEnvironment.FolderPath;
            }
            else if (!string.IsNullOrWhiteSpace(localFolder))
            {
                environmentFolder = System.IO.Path.Combine(localFolder, selectedEnvironment.Name);
            }
            else
            {
                _toastService.ShowToast($"Будь ласка, оберіть папку StarCitizen перед {operationDescription}.");
                return false;
            }

            if (!Directory.Exists(environmentFolder))
            {
                _toastService.ShowToast("Обрану папку середовища не знайдено. Оновіть список середовищ.");
                return false;
            }

            return true;
        }

        private void BtnReset_Cash(object sender, RoutedEventArgs e)
        {
            //очистка кешу логіка.
            _toastService.ShowToast("Кеш очищено.");
        }

        //================================ Button_Clicked Logic ================================

        //-------------------------------- кнопки панелі ---------------------------------------
        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        //---------------------------------кнопки меню навігації програми------------------------
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

        //-------------------------------- кнопки встановлення локалізації ------------------------------

        // ручний вибір папки локалізації
        private async void BtnSelectFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                localFolder = dialog.SelectedPath;
                TxtSelectedPath.Text = localFolder;

                Settings.Default.StarCitizenUA = localFolder;
                Settings.Default.Save();

                _toastService.ShowToast($"Вибрано папку: {localFolder}");

                isPathSet = true;

                BtnAutoSearch.ApplyTemplate();
                _buttonHelper.SetButtonState(BtnAutoSearch, isPathSet);

                _buttonStateManager.SetButtonEnabled(BtnSelectFolder, false);

                // ► оновити список папок/версій у селекторі
                if (EnvSelector != null)
                    await EnvSelector.UpdateFromGameFolderAsync(localFolder);
            }
        }

        //Автопошук папки локалізації
        private async void BtnAutoSearch_Click(object sender, RoutedEventArgs e)
        {
            BtnAutoSearch.ApplyTemplate();
            var isActive = !isPathSet;

            if (isActive)
            {
                var foundFolder = await _searchFolder.FindGameFolder(4);

                if (!string.IsNullOrEmpty(foundFolder))
                {
                    localFolder = foundFolder;
                    TxtSelectedPath.Text = foundFolder;
                    Settings.Default.StarCitizenUA = foundFolder;
                    Settings.Default.Save();
                    _toastService.ShowToast($"Знайдено папку: {foundFolder}");
                }
                else
                {
                    _toastService.ShowToast("Не вдалося знайти папку. Будь ласка, оберіть вручну.");
                    return;
                }
            }
            else
            {
                localFolder = string.Empty;
                TxtSelectedPath.Text = DefaultPathText;
                Settings.Default.StarCitizenUA = string.Empty;
                Settings.Default.Save();
                _toastService.ShowToast("Збережений шлях успішно скинуто.");
            }

            isPathSet = isActive;
            _buttonHelper.SetButtonState(BtnAutoSearch, isPathSet);
            _buttonStateManager.SetButtonEnabled(BtnSelectFolder, !isPathSet);

            // Оновлюємо список папок у селекторі
            if (EnvSelector != null)
                await EnvSelector.UpdateFromGameFolderAsync(isPathSet ? localFolder : null);
        }

        //встановлення локалізації
        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetSelectedEnvironmentFolder(out var environmentFolder, out var environmentName, "встановлення локалізації"))
            {
                return;
            }

            BtnInstall.IsEnabled = false;

            var env = EnvSelector.SelectedEnvironment;

            if (env == null || string.IsNullOrWhiteSpace(env.FolderPath))
            {
                _toastService.ShowToast("Будь ласка, оберіть середовище перед встановленням.");
                return;
            }

            try
            {
                var result = await _localizationInstaller.InstallAsync(env.FolderPath, env.Name);

                _toastService.ShowToast(result.Message);
            }
            catch (Exception ex)
            {
                _toastService.ShowToast($"Помилка: {ex.Message}");
            }
            finally
            {
                BtnInstall.IsEnabled = true;
                BtnInstall.Content = _buttonHelper.GetInstallButtonText(EnvSelector?.SelectedEnvironment, localFolder);
            }
        }

        //Видалення локалізації
        private async void LocalisationDelete_Click(object sender, RoutedEventArgs e)
        {
            var env = EnvSelector.SelectedEnvironment;

            if (env == null || string.IsNullOrWhiteSpace(env.FolderPath))
            {
                _toastService.ShowToast("Будь ласка, оберіть середовище для видалення локалізації.");
                return;
            }

            try
            {
                var result = await _localizationInstaller.DeleteAsync(env.FolderPath, env.Name);

                BtnInstall.Content = _buttonHelper.GetInstallButtonText(EnvSelector?.SelectedEnvironment, localFolder);
                _toastService.ShowToast(result.Message);
            }
            catch (Exception ex)
            {
                _toastService.ShowToast($"Помилка при видаленні локалізації: {ex.Message}");
            }
        }        

        //-----------------------------кнопки встановлення LIA ------------------------------

        // ручний вибір папки для інсталяції LIA
        private void BtnSelectLiaFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                localLiaFolder = dialog.SelectedPath;
                TxtLiaSelectedPath.Text = localLiaFolder;

                Settings.Default.StarCitizenLIA = localLiaFolder;
                Settings.Default.Save();

                _toastService.ShowToast($"Вибрано папку: {localLiaFolder}");

                isLiaPathSet = true;

                BtnLiaAutoSearch.ApplyTemplate();
                _buttonHelper.SetButtonState(BtnLiaAutoSearch, isLiaPathSet);

                _buttonStateManager.SetButtonEnabled(BtnSelectLiaFolder, false);
            }
        }

        //автопошук папки для інсталяції LIA
        private async void BtnLiaAutoSearch_Click(object sender, RoutedEventArgs e)
        {
            BtnLiaAutoSearch.ApplyTemplate();
            var isActive = !isLiaPathSet;

            if (isActive)
            {
                var foundFolder = await _voiceAttackFolderHelper.FindVoiceAttackImportFolderAsync();

                if (!string.IsNullOrEmpty(foundFolder))
                {
                    localLiaFolder = foundFolder;
                    TxtLiaSelectedPath.Text = foundFolder;
                    Settings.Default.StarCitizenLIA = foundFolder;
                    Settings.Default.Save();
                    _toastService.ShowToast($"Знайдено папку: {foundFolder}");
                }
                else
                {
                    _toastService.ShowToast("Не вдалося знайти папку. Будь ласка, оберіть вручну.");
                    return;
                }
            }
            else
            {
                localLiaFolder = string.Empty;
                TxtLiaSelectedPath.Text = DefaultPathText;
                Settings.Default.StarCitizenLIA = string.Empty;
                Settings.Default.Save();
                _toastService.ShowToast("Збережений шлях успішно скинуто.");
            }

            isLiaPathSet = isActive;
            _buttonHelper.SetButtonState(BtnLiaAutoSearch, isLiaPathSet);
            _buttonStateManager.SetButtonEnabled(BtnSelectLiaFolder, !isLiaPathSet);
        }

        //Встановлення LIA
        private void BtnLiaInstall_Click(object sender, RoutedEventArgs e)
        {
            _toastService.ShowToast("Функція встановлення LIA ще не реалізована.");
        }

        //======================================================================================
    }
}