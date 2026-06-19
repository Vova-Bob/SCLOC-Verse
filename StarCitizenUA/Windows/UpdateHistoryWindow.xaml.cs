using StarCitizenUA.Interfaces;
using StarCitizenUA.Models.ApplicationUpdate;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

namespace StarCitizenUA.Windows
{
    public partial class UpdateHistoryWindow : Window
    {
        private readonly IUpdateHistoryService _updateHistoryService;

        public UpdateHistoryWindow(IUpdateHistoryService updateHistoryService)
        {
            InitializeComponent();

            _updateHistoryService = updateHistoryService ?? throw new ArgumentNullException(nameof(updateHistoryService));

            Loaded += UpdateHistoryWindow_Loaded;
            RefreshButton.Click += RefreshButton_Click;
            ClearButton.Click += ClearButton_Click;
            CloseButton.Click += CloseButton_Click;
        }

        private async void UpdateHistoryWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadHistoryAsync().ConfigureAwait(true);
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadHistoryAsync().ConfigureAwait(true);
        }

        private async void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Очистити всю історію оновлень?",
                "Підтвердження",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            await _updateHistoryService.ClearAsync().ConfigureAwait(true);
            await LoadHistoryAsync().ConfigureAwait(true);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async Task LoadHistoryAsync()
        {
            try
            {
                var history = await _updateHistoryService.GetHistoryAsync().ConfigureAwait(true);
                HistoryGrid.ItemsSource = history;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Помилка завантаження історії",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                HistoryGrid.ItemsSource = Array.Empty<UpdateHistoryEntry>();
            }
        }
    }
}
