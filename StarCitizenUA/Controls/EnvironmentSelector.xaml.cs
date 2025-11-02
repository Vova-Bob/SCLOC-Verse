using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace StarCitizenUA.Controls
{
    public partial class EnvironmentSelector : UserControl
    {
        // Публічна подія для кліку по шестерні (піднімаємо назовні)
        public event EventHandler? GearClicked;

        // Колекція середовищ (LIVE, PTU, EPTU тощо)
        public ObservableCollection<EnvironmentOption> Environments { get; } = new();

        public EnvironmentSelector()
        {
            InitializeComponent();
            // Щоб Binding в XAML працював: ItemsSource="{Binding Environments, RelativeSource={RelativeSource AncestorType=UserControl}}"
            DataContext = this;
            AddHandler(Button.ClickEvent, new RoutedEventHandler(OnAnyButtonClick), handledEventsToo: true);
        }

        // Фільтр кліку саме по кнопці з Name="GearButton" у шаблоні
        private void OnAnyButtonClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (e.OriginalSource is DependencyObject d)
                {
                    var btn = FindAncestorButtonByName(d, "GearButton");
                    if (btn != null)
                    {
                        GearClicked?.Invoke(this, EventArgs.Empty);
                        e.Handled = true;
                    }
                }
            }
            catch
            {
                // ігноруємо, щоб не падало UI
            }
        }

        // Допоміжний пошук предка-Button за Name
        private static Button? FindAncestorButtonByName(DependencyObject start, string name)
        {
            DependencyObject? cur = start;
            while (cur != null)
            {
                if (cur is Button b && b.Name == name) return b;
                cur = System.Windows.Media.VisualTreeHelper.GetParent(cur);
            }
            return null;
        }

        /// Оновлює список середовищ згідно з реальною структурою папок гри.
        /// Якщо gameRoot порожній або не існує — список очищається.
        public async Task UpdateFromGameFolderAsync(string? gameRoot)
        {
            Environments.Clear();

            if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
            {
                return;
            }

            string[] envNames = new[] { "LIVE", "PTU", "HOTFIX", "EPTU" };

            foreach (var name in envNames)
            {
                string envDir = Path.Combine(gameRoot, name);
                if (Directory.Exists(envDir))
                {
                    // важливо: передаємо і назву папки (envName)
                    string version = await GetVersionAsync(envDir, name);
                    Environments.Add(new EnvironmentOption
                    {
                        Name = name,
                        Version = version
                    });
                }
            }

            // Автовибір першого елемента
            if (Environments.Count > 0 && EnvironmentComboBox != null)
                EnvironmentComboBox.SelectedIndex = 0;
        }

        // Метод отримує версію гри та додає назву папки (LIVE/PTU/...)
        private static async Task<string> GetVersionAsync(string envDir, string envName)
        {
            try
            {
                string idFile = Path.Combine(envDir, "f_win_game_client_release.id");
                if (!File.Exists(idFile))
                    return "версія невідома";

                using var fs = File.OpenRead(idFile);
                using var doc = await JsonDocument.ParseAsync(fs);

                if (!doc.RootElement.TryGetProperty("Data", out var data))
                    return "версія невідома";

                // ▪ зчитуємо поля з JSON
                string? branch = data.TryGetProperty("Branch", out var b) ? b.GetString() : null;
                string? change = data.TryGetProperty("RequestedP4ChangeNum", out var c) ? c.GetString() : null;

                if (string.IsNullOrWhiteSpace(branch) || string.IsNullOrWhiteSpace(change))
                    return "версія невідома";

                branch = branch.Replace("sc-alpha", "").TrimStart('-');
                if (branch.Contains('-'))
                    branch = branch.Split('-')[0];

                branch = $"{branch}-{envName.ToUpper()}";

                return $"{branch}.{change}";
            }
            catch
            {
                return "версія невідома";
            }
        }
    }

    public class EnvironmentOption
    {
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }
}
