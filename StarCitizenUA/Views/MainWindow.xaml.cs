using StarCitizenUA.Helpers;
using StarCitizenUA.Interfaces;
using StarCitizenUA.Services;
using StarCitizenUA.Services.LiaServices;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        private ILocalizationInstaller _localizationInstaller;
        private string? localFolder = "";
        private string? localLiaFolder = "";
        private bool isPathSet = false;
        private bool isLiaPathSet = false;
        private bool isSettingButtonClicked = false;

        private string DefaultPathText = "";

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

            BtnAutoSearch.Loaded += (s, e) => SetAutoSearchButtonState(isPathSet);
            BtnLiaAutoSearch.Loaded += (s, e) => SetLiaAutoSearchButtonState(isLiaPathSet);
            Loaded += MainWindow_Loaded;
            EnvSelector.GearClicked += EnvSelector_GearClicked;
            EnvSelector.SelectedEnvironmentChanged += (s, e) => UpdateInstallButtonText();
        }

        private void EnvSelector_SelectionChanged(object? sender, EventArgs e)
        {
            UpdateInstallButtonText();
        }

        public void Read()
        {
            string jsonPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PathText.json");
            var jsonService = new JsonService(jsonPath);
            var readmeData = jsonService.LoadReadme();

            TxtReadme.Text = readmeData.ReadmeText;
            TxtSelectedPath.Text = readmeData.TxtSelectedPath;
            DefaultPathText = readmeData.DefaultPathText;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _windowHelper.ApplyWindowRoundCorners(this);
            MainGrid.MouseMove += (s, e2) => _windowHelper.HandleMouseMove(this, bgImage, e2.GetPosition(MainGrid), MainGrid);
            MainGrid.MouseLeave += (s, e2) => _windowHelper.HandleMouseLeave(this, bgImage, MainGrid);
            Read();
            BtnAutoSearch.ApplyTemplate();
            BtnLiaAutoSearch.ApplyTemplate();
            string savedPath = Settings.Default.StarCitizenUA;
            string savedLiaPath = Settings.Default.StarCitizenLIA;

            if (!string.IsNullOrEmpty(savedPath))
            {
                localFolder = savedPath;
                isPathSet = true;
                TxtSelectedPath.Text = savedPath;

                SetAutoSearchButtonState(true);
                UpdateSelectButtonState(false);

                // ► підвантажуємо реальні папки/версії у селектор
                if (EnvSelector != null)
                    await EnvSelector.UpdateFromGameFolderAsync(localFolder);
                UpdateInstallButtonText();
            }
            else
            {
                localFolder = string.Empty;
                isPathSet = false;
                TxtSelectedPath.Text = DefaultPathText;

                SetAutoSearchButtonState(false);
                UpdateSelectButtonState(true);
            }
            if (!string.IsNullOrEmpty(savedLiaPath))
            {
                localLiaFolder = savedLiaPath;
                isLiaPathSet = true;
                TxtLiaSelectedPath.Text = savedLiaPath;

                SetLiaAutoSearchButtonState(true);
                UpdateLiaSelectButtonState(false);
            }
            else
            {
                localLiaFolder = string.Empty;
                isLiaPathSet = false;
                TxtLiaSelectedPath.Text = DefaultPathText;

                SetLiaAutoSearchButtonState(false);
                UpdateLiaSelectButtonState(true);
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

        //private void ShowCanvas(string which)
        //{
        //    CanvasHome.Visibility = Visibility.Collapsed;
        //    CanvasLocalization.Visibility = Visibility.Collapsed;
        //    CanvasAssistant.Visibility = Visibility.Collapsed;
        //    CanvasSettings.Visibility = Visibility.Collapsed;
        //    CanvasLiaSettings.Visibility = Visibility.Collapsed;

        //    switch (which)
        //    {
        //        case "home":
        //            CanvasHome.Visibility = Visibility.Visible;
        //            break;
        //        case "localization":
        //            CanvasLocalization.Visibility = Visibility.Visible;
        //            break;
        //        case "assistant":
        //            CanvasAssistant.Visibility = Visibility.Visible;
        //            break;
        //        case "settings":
        //            CanvasSettings.Visibility = Visibility.Visible;
        //            break;
        //        case "liasettings":
        //            CanvasLiaSettings.Visibility = Visibility.Visible;
        //            break;
        //    }

        //    SetActiveButton(which);
        //}

        private void SetActiveButton(string active)
        {
            BtnLocalization.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#143A52"));
            BtnAssistant.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#143A52"));
            BtnSettings.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#143A52"));

            switch (active)
            {
                case "localization":
                    BtnLocalization.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5190C3"));
                    break;
                case "assistant":
                    BtnAssistant.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5190C3"));
                    break;
                case "settings":
                    BtnSettings.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#5190C3"));
                    break;
            }
        }

        private void UpdateSelectButtonState(bool enabled)
        {
            BtnSelectFolder.IsEnabled = enabled; // вимкнути/увімкнути кнопку
            BtnSelectFolder.Opacity = enabled ? 1.0 : 0.5; // затемнити якщо неактивна
        }
        private void UpdateLiaSelectButtonState(bool enabled)
        {
            BtnSelectLiaFolder.IsEnabled = enabled; // вимкнути/увімкнути кнопку
            BtnSelectLiaFolder.Opacity = enabled ? 1.0 : 0.5; // затемнити якщо неактивна
        }

        private async void BtnAutoSearch_Click(object sender, RoutedEventArgs e)
        {
            if (!isPathSet)
            {
                var foundFolder = await _searchFolder.FindGameFolder(4);

                if (!string.IsNullOrEmpty(foundFolder))
                {
                    localFolder = foundFolder;
                    TxtSelectedPath.Text = foundFolder;

                    Settings.Default.StarCitizenUA = foundFolder;
                    Settings.Default.Save();

                    _toastService.ShowToast($"Знайдено папку: {foundFolder}");

                    SetAutoSearchButtonState(true);
                    UpdateSelectButtonState(false);

                    // ► оновити список папок/версій
                    if (EnvSelector != null)
                        await EnvSelector.UpdateFromGameFolderAsync(localFolder);
                }
                else
                {
                    _toastService.ShowToast("Не вдалося знайти папку. Будь ласка, оберіть вручну.");
                }
            }
            else
            {
                localFolder = string.Empty;
                TxtSelectedPath.Text = DefaultPathText;

                Settings.Default.StarCitizenUA = string.Empty;
                Settings.Default.Save();

                _toastService.ShowToast("Збережений шлях успішно скинуто.");

                SetAutoSearchButtonState(false);
                UpdateSelectButtonState(true);

                // ► очистити селектор
                if (EnvSelector != null)
                    await EnvSelector.UpdateFromGameFolderAsync(null);
            }
        }

        private void SetAutoSearchButtonState(bool pathSet)
        {
            isPathSet = pathSet;

            var btnText = (TextBlock)BtnAutoSearch.Template.FindName("BtnText", BtnAutoSearch);
            var bgPath = (System.Windows.Shapes.Path)BtnAutoSearch.Template.FindName("BgPath", BtnAutoSearch);

            if (btnText == null || bgPath == null) return;

            if (pathSet)
            {
                btnText.Text = "Скинути";
                bgPath.Fill = new SolidColorBrush(Colors.IndianRed);
            }
            else
            {
                btnText.Text = "Автопошук";
                bgPath.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#30546E")!);
            }
        }

        private void SetLiaAutoSearchButtonState(bool liaPathSet)
        {
            isLiaPathSet = liaPathSet;

            var btnText = (TextBlock)BtnLiaAutoSearch.Template.FindName("BtnText", BtnLiaAutoSearch);
            var bgPath = (System.Windows.Shapes.Path)BtnLiaAutoSearch.Template.FindName("BgPath", BtnLiaAutoSearch);

            if (btnText == null || bgPath == null) return;

            if (liaPathSet)
            {
                btnText.Text = "Скинути";
                bgPath.Fill = new SolidColorBrush(Colors.IndianRed);
            }
            else
            {
                btnText.Text = "Автопошук";
                bgPath.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#30546E")!);
            }
        }

        private void UpdateInstallButtonText()
        {
            if (BtnInstall == null)
            {
                return;
            }

            var selectedEnvironment = EnvSelector?.SelectedEnvironment;
            if (selectedEnvironment == null)
            {
                BtnInstall.Content = "Встановити";
                return;
            }

            string? environmentFolder = null;

            if (!string.IsNullOrWhiteSpace(selectedEnvironment.FolderPath))
            {
                environmentFolder = selectedEnvironment.FolderPath;
            }
            else if (!string.IsNullOrWhiteSpace(localFolder))
            {
                environmentFolder = System.IO.Path.Combine(localFolder, selectedEnvironment.Name);
            }

            if (string.IsNullOrWhiteSpace(environmentFolder) || !Directory.Exists(environmentFolder))
            {
                BtnInstall.Content = "Встановити";
                return;
            }

            string userCfgPath = System.IO.Path.Combine(environmentFolder, "user.cfg");
            BtnInstall.Content = File.Exists(userCfgPath) ? "Оновити" : "Встановити";
        }

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

        //private Canvas? GetCurrentVisibleCanvas()
        //{
        //    if (CanvasHome.Visibility == Visibility.Visible) return CanvasHome;
        //    if (CanvasLocalization.Visibility == Visibility.Visible) return CanvasLocalization;
        //    if (CanvasAssistant.Visibility == Visibility.Visible) return CanvasAssistant;
        //    if (CanvasSettings.Visibility == Visibility.Visible) return CanvasSettings;
        //    if (CanvasLiaSettings.Visibility == Visibility.Visible) return CanvasLiaSettings;
        //    return null;
        //}

        private void BtnReset_Cash(object sender, RoutedEventArgs e)
        {
            //очистка кешу логіка.
            _toastService.ShowToast("Кеш очищено.");
        }

        //public void SwitchCanvas(Canvas showCanvas, Canvas? hideCanvas, double durationSeconds = 0.3)
        //{
        //    if (showCanvas == null) return;

        //    // Якщо є Canvas для сховання
        //    if (hideCanvas != null)
        //    {
        //        DoubleAnimation fadeOut = new DoubleAnimation(0, TimeSpan.FromSeconds(durationSeconds));
        //        fadeOut.Completed += (s, e) =>
        //        {
        //            hideCanvas.Visibility = Visibility.Collapsed;

        //            // Після того, як сховали старий, показуємо новий
        //            showCanvas.Opacity = 0;
        //            showCanvas.Visibility = Visibility.Visible;
        //            DoubleAnimation fadeIn = new DoubleAnimation(1, TimeSpan.FromSeconds(durationSeconds));
        //            showCanvas.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        //        };
        //        hideCanvas.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        //    }
        //    else
        //    {
        //        // Якщо нічого сховати, просто плавно показати
        //        showCanvas.Opacity = 0;
        //        showCanvas.Visibility = Visibility.Visible;
        //        DoubleAnimation fadeIn = new DoubleAnimation(1, TimeSpan.FromSeconds(durationSeconds));
        //        showCanvas.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        //    }
        //}

        //================================ Button_Clicked Logic ================================

        //-------------------------------- кнопки панелі ---------------------------------------
        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        //---------------------------------кнопки меню навігації програми------------------------
        private void Localization_Click(object sender, RoutedEventArgs e)
        {
            _canvasManager.SwitchCanvas(CanvasLocalization);
            SetActiveButton("localization");
            isSettingButtonClicked = false;
        }
        private void Assistant_Click(object sender, RoutedEventArgs e)
        {
            _canvasManager.SwitchCanvas(CanvasAssistant);
            SetActiveButton("assistant");
            isSettingButtonClicked = false;
        }
        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            _canvasManager.SwitchCanvas(CanvasSettings);
            SetActiveButton("settings");
            isSettingButtonClicked = true;
        }

        private void LocalisationSettings_Click(object sender, RoutedEventArgs e)
        {
            _canvasManager.SwitchCanvas(CanvasSettings);
            SetActiveButton("settings");
        }

        private void ReturnToHome_Click(object sender, RoutedEventArgs e)
        {
            _canvasManager.SwitchCanvas(CanvasHome);
            SetActiveButton("home");
            isSettingButtonClicked = false;
        }

        private void ReturnToLocalization_Click(object sender, RoutedEventArgs e)
        {
            if (isSettingButtonClicked)
            {
                _canvasManager.SwitchCanvas(CanvasHome);
                SetActiveButton("home");
                isSettingButtonClicked = false;
            }
            else
            {
                _canvasManager.SwitchCanvas(CanvasLocalization);
                SetActiveButton("localization");
            }
        }

        private void ReturnToAssistant_Click(object sender, RoutedEventArgs e)
        {
            _canvasManager.SwitchCanvas(CanvasAssistant);
            SetActiveButton("assistant");
        }

        private void EnvSelector_GearClicked(object? sender, EventArgs e)
        {
            _canvasManager.SwitchCanvas(CanvasSettings);
        }

        private void LiaSettings_Click(object sender, RoutedEventArgs e)
        {
            _canvasManager.SwitchCanvas(CanvasLiaSettings);
            SetActiveButton("assistant");
        }

        //-------------------------------- кнопки встановлення локалізації ------------------------------

        // Обробник кнопки вибору папки локалізації вручну
        private async void BtnSelectFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                localLiaFolder = dialog.SelectedPath;
                TxtSelectedPath.Text = localLiaFolder;

                Settings.Default.StarCitizenUA = localLiaFolder;
                Settings.Default.Save();

                _toastService.ShowToast($"Вибрано папку: {localLiaFolder}");

                SetAutoSearchButtonState(true);

                // ► оновити список папок/версій
                if (EnvSelector != null)
                    await EnvSelector.UpdateFromGameFolderAsync(localLiaFolder);
            }
        }

        //Встановлення локалізації
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
                UpdateInstallButtonText();
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

                UpdateInstallButtonText();
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

                SetLiaAutoSearchButtonState(true);
            }
        }

        //автопошук папки для інсталяції LIA
        private async void BtnLiaAutoSearch_Click(object sender, RoutedEventArgs e)
        {
            if (!isLiaPathSet)
            {
                var foundFolder = await _voiceAttackFolderHelper.FindVoiceAttackImportFolderAsync();

                if (!string.IsNullOrEmpty(foundFolder))
                {
                    localLiaFolder = foundFolder;
                    TxtLiaSelectedPath.Text = foundFolder;

                    Settings.Default.StarCitizenLIA = foundFolder;
                    Settings.Default.Save();

                    _toastService.ShowToast($"Знайдено папку: {foundFolder}");

                    SetLiaAutoSearchButtonState(true);
                    UpdateLiaSelectButtonState(false);
                }
                else
                {
                    _toastService.ShowToast("Не вдалося знайти папку. Будь ласка, оберіть вручну.");
                }
            }
            else
            {
                localLiaFolder = string.Empty;
                TxtLiaSelectedPath.Text = DefaultPathText;

                Settings.Default.StarCitizenLIA = string.Empty;
                Settings.Default.Save();

                _toastService.ShowToast("Збережений шлях успішно скинуто.");

                SetLiaAutoSearchButtonState(false);
                UpdateLiaSelectButtonState(true);
            }
        }

        //Встановлення LIA
        private void BtnLiaInstall_Click(object sender, RoutedEventArgs e)
        {
            _toastService.ShowToast("Функція встановлення LIA ще не реалізована.");
        }


        //======================================================================================
    }
}