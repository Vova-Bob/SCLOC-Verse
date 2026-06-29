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

        private void PopulateTools()
        {
            var cards = new ObservableCollection<HangarTimerCard>
            {
                CreateHangarTimerCard()
            };

            ToolsList.ItemsSource = cards;
        }

        private HangarTimerCard CreateHangarTimerCard()
        {
            var card = new HangarTimerCard();
            if (_hangarTimerService != null)
                card.SetHangarTimerService(_hangarTimerService);
            return card;
        }
    }
}
