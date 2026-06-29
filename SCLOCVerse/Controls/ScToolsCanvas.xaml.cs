using SCLOCVerse.Interfaces;
using System.Collections.ObjectModel;
using System.Windows.Controls;

namespace SCLOCVerse.Controls
{
    /// <summary>
    /// Сторінка «Інструменти SC» — центр майбутніх інструментів Star Citizen.
    /// </summary>
    public partial class ScToolsCanvas : Canvas
    {
        private IHangarTimerService? _hangarTimerService;

        /// <summary>
        /// Модель картки інструменту для легкого розширення списку в майбутньому.
        /// </summary>
        public class ToolCard
        {
            public string Icon { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string ButtonText { get; set; } = string.Empty;
            public Action? LaunchAction { get; set; }
        }

        public ScToolsCanvas()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        public void SetHangarTimerService(IHangarTimerService service)
        {
            _hangarTimerService = service;
            PopulateTools();
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_hangarTimerService != null)
                PopulateTools();
        }

        private void ToolButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is ToolCard card)
                card.LaunchAction?.Invoke();
        }

        private void PopulateTools()
        {
            var tools = new ObservableCollection<ToolCard>
            {
                new ToolCard
                {
                    Icon = "\uE7C4",
                    Title = "Hangar Timer",
                    Description = "Оверлей циклу Executive Hangar із таймером та LED-індикаторами.",
                    ButtonText = "Запустити",
                    LaunchAction = () => _hangarTimerService?.ToggleOverlayAsync()
                }
            };

            ToolsList.ItemsSource = tools;
        }
    }
}
