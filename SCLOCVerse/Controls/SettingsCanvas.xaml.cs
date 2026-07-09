using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SCLOCVerse.Controls.SettingsHub;

namespace SCLOCVerse.Controls
{
    /// <summary>
    /// Settings Hub — Центральна оболонка налаштувань.
    /// Ліва панель навігації категорій + права панель вмісту.
    /// Зберігає фасадну property-поверхню (мігровані контролі «Загальне»),
    /// щоб MainWindow.xaml.cs продовжував звертатись до CanvasSettings.* без змін.
    /// </summary>
    public partial class SettingsCanvas : Canvas
    {
        private static readonly Brush ActiveNavBackground = new SolidColorBrush(Color.FromRgb(0x1A, 0x3D, 0x58));
        private static readonly Brush ActiveNavAccent = new SolidColorBrush(Color.FromRgb(0xE3, 0x6D, 0x3A)); // помаранчевий P0
        private static readonly Brush InactiveNavBackground = Brushes.Transparent;
        private static readonly Brush InactiveNavBorder = Brushes.Transparent;
        private static readonly Brush ActiveNavForeground = new SolidColorBrush(Color.FromRgb(0xEA, 0xF4, 0xFF));
        private static readonly Brush InactiveNavForeground = new SolidColorBrush(Color.FromRgb(0x9D, 0xBB, 0xD4));

        private FrameworkElement[] _panes = null!;
        private Button[] _navButtons = null!;

        public SettingsCanvas()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Ініціалізує внутрішні масиви навігації. Викликається з MainWindow
        /// після завантаження (коли всі елементи вже створені).
        /// </summary>
        public void InitializeHubNavigation()
        {
            _panes = new FrameworkElement[]
            {
                PaneGeneral, PaneLocalization, PaneInterface,
                PaneHotkeys, PaneOverlay, PaneProfile, PaneAbout
            };

            _navButtons = new Button[]
            {
                NavGeneral, NavLocalization, NavInterface,
                NavHotkeys, NavOverlay, NavProfile, NavAbout
            };

            // Типовий активний пункт — «Загальне».
            ActivateCategory(PaneGeneral, NavGeneral);
        }

        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not FrameworkElement target)
                return;

            ActivateCategory(target, btn);
        }

        private void ActivateCategory(FrameworkElement targetPane, Button navButton)
        {
            foreach (var pane in _panes)
                pane.Visibility = pane == targetPane ? Visibility.Visible : Visibility.Collapsed;

            foreach (var b in _navButtons)
            {
                bool active = b == navButton;
                b.Background = active ? ActiveNavBackground : InactiveNavBackground;
                b.BorderBrush = active ? ActiveNavAccent : InactiveNavBorder;
                b.BorderThickness = active ? new Thickness(3, 0, 0, 0) : new Thickness(0);
                b.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
                b.Foreground = active ? ActiveNavForeground : InactiveNavForeground;
            }
        }

        // ============ ФАСАД для MainWindow.xaml.cs (Zero Regression) ============
        // Реальні контролі мігрували у GeneralSettingsPane; тут лише делегування.

        public Button ReturnHomeButton => BtnReturnHome;

        public Button SelectFolderButton => PaneGeneral.SelectFolderButton;

        public Button AutoSearchButton => PaneGeneral.AutoSearchButton;

        public Button ResetCacheButton => PaneGeneral.ResetCacheButton;

        public TextBox SelectedPathTextBox => PaneGeneral.SelectedPathTextBox;

        public TextBox ReadmeTextBox => PaneGeneral.ReadmeTextBox;

        public ComboBox UpdateChannelSelector => PaneGeneral.UpdateChannelSelector;

        public Button UpdateHistoryButtonControl => PaneGeneral.UpdateHistoryButtonControl;

        public CheckBox RunAtStartupCheckBoxControl => PaneGeneral.RunAtStartupCheckBoxControl;

        public CheckBox MinimizeToTrayCheckBoxControl => PaneGeneral.MinimizeToTrayCheckBoxControl;

        public CheckBox AutoUpdateLocalizationCheckBoxControl => PaneGeneral.AutoUpdateLocalizationCheckBoxControl;

        public CheckBox AdvancedDiagnosticsCheckBoxControl => PaneGeneral.AdvancedDiagnosticsCheckBoxControl;

        /// <summary>Доступ до панелі «Загальне» (для майбутніх розширень Hub).</summary>
        public GeneralSettingsPane GeneralPane => PaneGeneral;

        /// <summary>Доступ до панелі «Гарячі клавіші».</summary>
        public HotkeysSettingsPane? HotkeysPane => PaneHotkeys;

        /// <summary>Доступ до панелі «Overlay».</summary>
        public OverlaySettingsPane? OverlayPane => PaneOverlay;
    }
}
