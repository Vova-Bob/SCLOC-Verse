using SCLOCVerse.Interfaces;
using SCLOCVerse.Services.InputSystem;
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
        private IHotkeyService? _hotkeyService;

        public ScToolsCanvas()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        public Button ReturnHomeButton => BtnReturnHome;

        public void SetHangarTimerService(IHangarTimerService service)
        {
            _hangarTimerService = service;
            PopulateTools();
        }

        /// <summary>
        /// Передає сервіс гарячих клавіш для динамічних підказок карток (SSOT).
        /// </summary>
        public void SetHotkeyService(IHotkeyService service)
        {
            _hotkeyService = service;
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
            if (_hotkeyService != null)
                card.SetHotkeyService(_hotkeyService);
            return card;
        }
    }
}
