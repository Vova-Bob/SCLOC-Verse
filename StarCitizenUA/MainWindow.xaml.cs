using StarCitizenUA.Interfaces;
using StarCitizenUA.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace StarCitizenUA
{
    public partial class MainWindow : Window
    {
        private IWindowHelper _windowHelper;
        private ILinkService _linkService;
        private IToastService _toastService;
        private ISearchFolder _searchFolder;
        private IVoiceAttackFolderHelper _voiceAttackFolderHelper;
        private string? localFolder = "";
        private string? localLiaFolder = "";
        private bool isPathSet = false;
        private bool isLiaPathSet = false;
        private bool isSettingButtonClicked = false;

        private void Localization_Click(object sender, RoutedEventArgs e)
        {
            ShowCanvas("localization");
            isSettingButtonClicked = false;
        }
        private void Assistant_Click(object sender, RoutedEventArgs e)
        {
            ShowCanvas("assistant");
            isSettingButtonClicked = false;
        }
        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            ShowCanvas("settings");
            isSettingButtonClicked = true;
        }
        private void LocalisationSettings_Click(object sender, RoutedEventArgs e) => ShowCanvas("settings");
        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private string DefaultPathText;

        public MainWindow()
        {
            InitializeComponent();
            _toastService = new ToastService(ToastMessage, ToastText);
            _searchFolder = new SearchFolder(_toastService);
            _windowHelper = new WindowHelper();
            _linkService = new LinkService();
            _voiceAttackFolderHelper = new VoiceAttackFolderHelper(_toastService);

            BtnAutoSearch.Loaded += (s, e) => SetAutoSearchButtonState(isPathSet);
            BtnLiaAutoSearch.Loaded += (s, e) => SetLiaAutoSearchButtonState(isLiaPathSet);
            Loaded += MainWindow_Loaded;
            EnvSelector.GearClicked += EnvSelector_GearClicked;
        }

        private void EnvSelector_GearClicked(object? sender, EventArgs e)
        {
            ShowCanvas("settings");
        }

        public void Reed()
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
            Reed();
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

            ShowCanvas("home");
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

        private void ShowCanvas(string which)
        {
            CanvasHome.Visibility = Visibility.Collapsed;
            CanvasLocalization.Visibility = Visibility.Collapsed;
            CanvasAssistant.Visibility = Visibility.Collapsed;
            CanvasSettings.Visibility = Visibility.Collapsed;
            CanvasLiaSettings.Visibility = Visibility.Collapsed;

            switch (which)
            {
                case "home":
                    CanvasHome.Visibility = Visibility.Visible;
                    break;
                case "localization":
                    CanvasLocalization.Visibility = Visibility.Visible;
                    break;
                case "assistant":
                    CanvasAssistant.Visibility = Visibility.Visible;
                    break;
                case "settings":
                    CanvasSettings.Visibility = Visibility.Visible;
                    break;
                case "liasettings":
                    CanvasLiaSettings.Visibility = Visibility.Visible;
                    break;
            }

            SetActiveButton(which);
        }

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
            var bgPath = (Path)BtnAutoSearch.Template.FindName("BgPath", BtnAutoSearch);

            if (btnText == null || bgPath == null) return;

            if (pathSet)
            {
                btnText.Text = "Скинути";
                bgPath.Fill = new SolidColorBrush(Colors.IndianRed);
            }
            else
            {
                btnText.Text = "Автопошук";
                bgPath.Fill = (SolidColorBrush)(new BrushConverter().ConvertFromString("#30546E"));
            }
        }

        private void SetLiaAutoSearchButtonState(bool liaPathSet)
        {
            isLiaPathSet = liaPathSet;

            var btnText = (TextBlock)BtnLiaAutoSearch.Template.FindName("BtnText", BtnLiaAutoSearch);
            var bgPath = (Path)BtnLiaAutoSearch.Template.FindName("BgPath", BtnLiaAutoSearch);

            if (btnText == null || bgPath == null) return;

            if (liaPathSet)
            {
                btnText.Text = "Скинути";
                bgPath.Fill = new SolidColorBrush(Colors.IndianRed);
            }
            else
            {
                btnText.Text = "Автопошук";
                bgPath.Fill = (SolidColorBrush)(new BrushConverter().ConvertFromString("#30546E"));
            }
        }

        // Обробник кнопки вибору папки вручну
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

                SetAutoSearchButtonState(true);

                // ► оновити список папок/версій
                if (EnvSelector != null)
                    await EnvSelector.UpdateFromGameFolderAsync(localFolder);
            }
        }

        private void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            string path = TxtLiaSelectedPath.Text;
            if (string.IsNullOrWhiteSpace(path) || path == DefaultPathText)
            {
                _toastService.ShowToast("Будь ласка, оберіть папку перед встановленням.");
                return;
            }

            _toastService.ShowToast($"Встановлення локалізації у: {path}");

        }

        private void ReturnToHome_Click(object sender, RoutedEventArgs e)
        {
            CanvasHome.Visibility = Visibility.Visible;
            CanvasLocalization.Visibility = Visibility.Collapsed;
            CanvasAssistant.Visibility = Visibility.Collapsed;
            CanvasSettings.Visibility = Visibility.Collapsed;

            SetActiveButton("home");
        }

        private void ReturnToLocalization_Click(object sender, RoutedEventArgs e)
        {
            if (isSettingButtonClicked)
            {
                CanvasHome.Visibility = Visibility.Visible;
                CanvasLocalization.Visibility = Visibility.Collapsed;
                CanvasAssistant.Visibility = Visibility.Collapsed;
                CanvasSettings.Visibility = Visibility.Collapsed;
                SetActiveButton("home");
                isSettingButtonClicked = false;
            }
            else
            {
                CanvasHome.Visibility = Visibility.Collapsed;
                CanvasLocalization.Visibility = Visibility.Visible;
                CanvasAssistant.Visibility = Visibility.Collapsed;
                CanvasSettings.Visibility = Visibility.Collapsed;

                SetActiveButton("localization");
            }
        }

        private void ReturnToAssistant_Click(object sender, RoutedEventArgs e)
        {
            CanvasHome.Visibility = Visibility.Collapsed;
            CanvasLocalization.Visibility = Visibility.Collapsed;
            CanvasAssistant.Visibility = Visibility.Visible;
            CanvasSettings.Visibility = Visibility.Collapsed;
            CanvasLiaSettings.Visibility = Visibility.Collapsed;

            SetActiveButton("assistant");

        }

        private void BtnReset_Cash(object sender, RoutedEventArgs e)
        {
            //очистка кешу логіка.
            _toastService.ShowToast("Кеш очищено.");
        }

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

        private async void BtnLiaAutoSearch_Click(object sender, RoutedEventArgs e)
        {
            if (!isLiaPathSet)
            {
                var foundFolder = await _voiceAttackFolderHelper.FindVoiceAttackImportFolderAsync();

                if (!string.IsNullOrEmpty(foundFolder))
                {
                    localFolder = foundFolder;
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
                localFolder = string.Empty;
                TxtLiaSelectedPath.Text = DefaultPathText;

                Settings.Default.StarCitizenLIA = string.Empty;
                Settings.Default.Save();

                _toastService.ShowToast("Збережений шлях успішно скинуто.");

                SetLiaAutoSearchButtonState(false);
                UpdateLiaSelectButtonState(true);
            }
        }

        private void LiaSettings_Click(object sender, RoutedEventArgs e)
        {
            ShowCanvas("liasettings");
            SetActiveButton("assistant");
        }

        private void BtnLiaInstall_Click(object sender, RoutedEventArgs e)
        {

        }

        // Кнопка видалення локалізації (заглушка).
        private void LocalisationDelete_Click(object sender, RoutedEventArgs e) 
        {
            _toastService.ShowToast("Локалізацію видалено!.");
        }
    }
}