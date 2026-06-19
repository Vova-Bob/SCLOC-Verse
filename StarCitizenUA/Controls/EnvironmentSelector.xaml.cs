using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using StarCitizenUA.Models;

namespace StarCitizenUA.Controls
{
    public partial class EnvironmentSelector : UserControl
    {
        public event EventHandler? GearClicked;
        public event EventHandler? SelectedEnvironmentChanged;

        public ObservableCollection<EnvironmentOption> Environments { get; } = new();
        public EnvironmentOption? SelectedEnvironment => EnvironmentComboBox?.SelectedItem as EnvironmentOption;

        public EnvironmentSelector()
        {
            InitializeComponent();
            DataContext = this;
            AddHandler(Button.ClickEvent, new RoutedEventHandler(OnAnyButtonClick), handledEventsToo: true);
        }

        private void EnvironmentComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedEnvironmentChanged?.Invoke(this, EventArgs.Empty);
        }

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
            catch { }
        }

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

        public async Task UpdateFromGameFolderAsync(string? gameRoot)
        {
            Environments.Clear();
            if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot)) return;

            string[] envNames = new[] { "LIVE", "PTU", "HOTFIX", "EPTU" };
            foreach (var name in envNames)
            {
                string envDir = Path.Combine(gameRoot, name);
                if (Directory.Exists(envDir))
                {
                    string version = await GetVersionAsync(envDir, name);
                    Environments.Add(new EnvironmentOption
                    {
                        Name = name,
                        Version = version,
                        FolderPath = envDir
                    });
                }
            }

            if (EnvironmentComboBox != null)
                EnvironmentComboBox.SelectedIndex = Environments.Count > 0 ? 0 : -1;
        }

        private static async Task<string> GetVersionAsync(string envDir, string envName)
        {
            try
            {
                string idFile = ResolveVersionFilePath(envDir);
                if (string.IsNullOrEmpty(idFile)) return "версія невідома";

                using var fs = File.OpenRead(idFile);
                using var doc = await JsonDocument.ParseAsync(fs);

                if (!doc.RootElement.TryGetProperty("Data", out var data)) return "версія невідома";

                string? branch = data.TryGetProperty("Branch", out var b) ? b.GetString() : null;
                string? change = data.TryGetProperty("RequestedP4ChangeNum", out var c) ? c.GetString() : null;

                if (string.IsNullOrWhiteSpace(branch) || string.IsNullOrWhiteSpace(change))
                    return "версія невідома";

                branch = branch.Replace("sc-alpha", "").TrimStart('-');
                if (branch.Contains('-')) branch = branch.Split('-')[0];
                branch = $"{branch}-{envName.ToUpper()}";

                return $"{branch}.{change}";
            }
            catch { return "версія невідома"; }
        }

        private static string ResolveVersionFilePath(string envDir)
        {
            string buildManifest = Path.Combine(envDir, "build_manifest.id");
            if (File.Exists(buildManifest)) return buildManifest;

            string releaseId = Path.Combine(envDir, "f_win_game_client_release.id");
            if (File.Exists(releaseId)) return releaseId;

            return string.Empty;
        }

        //Перевірка вибраного середовища
        public bool TryGetSelectedEnvironment(out EnvironmentOption? env, out string? folderPath, out string? envName, string operationDescription)
        {
            env = SelectedEnvironment;
            folderPath = null;
            envName = null;

            if (env == null)
            {
                return false;
            }

            envName = env.Name;
            folderPath = !string.IsNullOrWhiteSpace(env.FolderPath) ? env.FolderPath : null;

            if (folderPath == null || !Directory.Exists(folderPath))
                return false;

            return true;
        }
    }
}
