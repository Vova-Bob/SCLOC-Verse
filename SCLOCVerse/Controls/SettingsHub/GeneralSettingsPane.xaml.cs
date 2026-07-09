using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SCLOCVerse.Helpers;

namespace SCLOCVerse.Controls.SettingsHub
{
    /// <summary>
    /// Панель категорії «Загальне» Settings Hub.
    /// Містить мігровані з SettingsCanvas контролі (ті самі x:Name),
    /// щоб MainWindow.xaml.cs продовжував працювати без змін (Zero Regression).
    /// </summary>
    public partial class GeneralSettingsPane : UserControl
    {
        public GeneralSettingsPane()
        {
            InitializeComponent();
        }

        // Відкриті контролі для фасаду SettingsCanvas → MainWindow.
        public Button SelectFolderButton => BtnSelectFolder;
        public Button AutoSearchButton => BtnAutoSearch;
        public Button ResetCacheButton => BtnResetCash;
        public TextBox SelectedPathTextBox => TxtSelectedPath;
        public TextBox ReadmeTextBox => TxtReadme;
        public ComboBox UpdateChannelSelector => UpdateChannelComboBox;
        public Button UpdateHistoryButtonControl => UpdateHistoryButton;
        public CheckBox RunAtStartupCheckBoxControl => RunAtStartupCheckBox;
        public CheckBox MinimizeToTrayCheckBoxControl => MinimizeToTrayCheckBox;
        public CheckBox AutoUpdateLocalizationCheckBoxControl => AutoUpdateLocalizationCheckBox;
        public CheckBox AdvancedDiagnosticsCheckBoxControl => AdvancedDiagnosticsCheckBox;

        /// <summary>
        /// Оновлює бейджі середовищ і стан у cat-head за реальним шляхом до гри.
        /// P0 — центр керування показує стан продукту.
        /// </summary>
        public void RefreshEnvironmentBadges(string? gameFolder)
        {
            EnvBadgesPanel.Children.Clear();
            var existing = StarCitizenEnvironments.DetectExisting(gameFolder);

            // Стан у cat-head.
            SubtitleText.Text = string.IsNullOrWhiteSpace(gameFolder)
                ? "шлях не задано"
                : existing.Length > 0
                    ? $"{existing.Length} середовищ знайдено"
                    : "шлях задано, середовищ не знайдено";

            if (existing.Length == 0)
                return;

            // Бейджі: знайдені середовища (зелена крапка + назва). Незнайдені не показуємо — лише реальний стан (P5).
            foreach (var env in StarCitizenEnvironments.Known.Where(e => existing.Contains(e)))
            {
                var dot = new Ellipse
                {
                    Width = 7,
                    Height = 7,
                    Fill = new SolidColorBrush(Color.FromRgb(0x5B, 0xC9, 0x8A)),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var label = new TextBlock
                {
                    Text = env,
                    FontFamily = new FontFamily("Segoe UI Semibold"),
                    FontSize = 10.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0xD8, 0xB0)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0)
                };

                var chip = new StackPanel { Orientation = Orientation.Horizontal };
                chip.Children.Add(dot);
                chip.Children.Add(label);

                var badge = new Border
                {
                    Style = (Style)FindResource("HubEnvBadgeFound"),
                    Child = chip,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                EnvBadgesPanel.Children.Add(badge);
            }
        }
    }
}
