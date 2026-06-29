using System.Collections.ObjectModel;
using System.Windows.Controls;

namespace SCLOCVerse.Controls
{
    /// <summary>
    /// Сторінка «Інструменти SC» — центр майбутніх інструментів Star Citizen.
    /// </summary>
    public partial class ScToolsCanvas : Canvas
    {
        /// <summary>
        /// Модель картки інструменту для легкого розширення списку в майбутньому.
        /// </summary>
        public class ToolCard
        {
            public string Icon { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string ButtonText { get; set; } = string.Empty;
        }

        public ScToolsCanvas()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            var tools = new ObservableCollection<ToolCard>
            {
                new ToolCard
                {
                    Icon = "\uEC7A",
                    Title = "Майбутній інструмент",
                    Description = "Тут з'явиться перший інструмент SCLOC-Verse.",
                    ButtonText = "Незабаром"
                }
            };

            ToolsList.ItemsSource = tools;
        }
    }
}
