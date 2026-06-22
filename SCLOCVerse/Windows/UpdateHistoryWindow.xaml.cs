using SCLOCVerse.Helpers;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.ApplicationUpdate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SCLOCVerse.Windows
{
    public partial class UpdateHistoryWindow : Window
    {
        private readonly IGitHubReleaseClient _gitHubReleaseClient;
        private readonly IApplicationVersionProvider _applicationVersionProvider;
        private readonly IUpdateChannelService _updateChannelService;
        private readonly ILinkService _linkService;
        private readonly IDialogService _dialogService;
        private readonly Action<GitHubRelease> _installRequested;
        private readonly string _owner = "Vova-Bob";
        private readonly string _repo = "SCLOC-Verse";

        public UpdateHistoryWindow(
            IGitHubReleaseClient gitHubReleaseClient,
            IApplicationVersionProvider applicationVersionProvider,
            IUpdateChannelService updateChannelService,
            ILinkService linkService,
            IDialogService dialogService,
            Action<GitHubRelease> installRequested)
        {
            InitializeComponent();

            _gitHubReleaseClient = gitHubReleaseClient ?? throw new ArgumentNullException(nameof(gitHubReleaseClient));
            _applicationVersionProvider = applicationVersionProvider ?? throw new ArgumentNullException(nameof(applicationVersionProvider));
            _updateChannelService = updateChannelService ?? throw new ArgumentNullException(nameof(updateChannelService));
            _linkService = linkService ?? throw new ArgumentNullException(nameof(linkService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _installRequested = installRequested ?? throw new ArgumentNullException(nameof(installRequested));

            Loaded += UpdateHistoryWindow_Loaded;
            CloseButton.Click += CloseButton_Click;
        }

        private async void UpdateHistoryWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadVersionsAsync().ConfigureAwait(true);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async Task LoadVersionsAsync()
        {
            try
            {
                var currentVersion = _applicationVersionProvider.GetCurrentVersion();
                var currentChannel = GetCurrentChannel();
                var releases = await _gitHubReleaseClient.GetReleasesAsync(_owner, _repo, CancellationToken.None).ConfigureAwait(true);

                var items = releases
                    .Where(r => IsChannelMatch(r, currentChannel))
                    .Where(r => VersionParser.TryParse(r.TagName, out _))
                    .Select(r => CreateVersionItem(r, currentVersion))
                    .OrderByDescending(i => i.ReleaseDate)
                    .ToList();

                VersionsGrid.ItemsSource = items;
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    $"РќРµ РІРґР°Р»РѕСЃСЏ Р·Р°РІР°РЅС‚Р°Р¶РёС‚Рё СЃРїРёСЃРѕРє РІРµСЂСЃС–Р№: {ex.Message}",
                    "РџРѕРјРёР»РєР°",
                    this).ConfigureAwait(true);

                VersionsGrid.ItemsSource = Array.Empty<VersionManagerItem>();
            }
        }

        private VersionManagerItem CreateVersionItem(GitHubRelease release, Version currentVersion)
        {
            var parsedVersion = VersionParser.Parse(release.TagName);
            var isInstalled = parsedVersion == currentVersion;
            var shortDescription = GetShortDescription(release.Body);

            return new VersionManagerItem
            {
                ReleaseDate = release.PublishedAt,
                Version = parsedVersion.ToString(),
                Description = shortDescription,
                FullDescription = release.Body,
                ReleaseUrl = $"https://github.com/{_owner}/{_repo}/releases/tag/{release.TagName}",
                IsInstalled = isInstalled,
                HasDetails = !string.IsNullOrWhiteSpace(release.Body) && release.Body.Length > shortDescription.Length,
                StatusText = isInstalled ? "Р’СЃС‚Р°РЅРѕРІР»РµРЅРѕ" : "Р’СЃС‚Р°РЅРѕРІРёС‚Рё",
                StatusColor = isInstalled ? "#4CAF50" : "#2196F3",
                Release = release,
                Channel = GetChannel(release)
            };
        }

        private static string GetShortDescription(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return string.Empty;

            var firstLine = body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            const int maxLength = 60;
            return firstLine.Length > maxLength ? firstLine.Substring(0, maxLength) + "..." : firstLine;
        }

        private UpdateChannel GetCurrentChannel()
        {
            var channelName = _updateChannelService.GetUpdateChannel();
            return Enum.TryParse<UpdateChannel>(channelName, out var channel) ? channel : UpdateChannel.Stable;
        }

        private static bool IsChannelMatch(GitHubRelease release, UpdateChannel channel)
        {
            return channel == UpdateChannel.Dev
                ? release.Prerelease
                : !release.Prerelease;
        }

        private static UpdateChannel GetChannel(GitHubRelease release)
        {
            return release.Prerelease ? UpdateChannel.Dev : UpdateChannel.Stable;
        }

        private void VersionsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Р†РіРЅРѕСЂСѓС”РјРѕ РїРѕРґРІС–Р№РЅРёР№ РєР»С–Рє вЂ” РІРёРєРѕСЂРёСЃС‚РѕРІСѓС”РјРѕ Р»РёС€Рµ СЏРІРЅСѓ РєРЅРѕРїРєСѓ Р’СЃС‚Р°РЅРѕРІРёС‚Рё
        }

        private void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not VersionManagerItem item || item.IsInstalled)
                return;

            _installRequested(item.Release);
            Close();
        }

        private async void DetailsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not VersionManagerItem item)
                return;

            if (!string.IsNullOrWhiteSpace(item.ReleaseUrl))
            {
                try
                {
                    await _linkService.OpenLinkAsync(item.ReleaseUrl).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    await _dialogService.ShowErrorAsync(
                        $"РќРµ РІРґР°Р»РѕСЃСЏ РІС–РґРєСЂРёС‚Рё РїРѕСЃРёР»Р°РЅРЅСЏ: {ex.Message}",
                        "РџРѕРјРёР»РєР°",
                        this).ConfigureAwait(true);
                }
            }
        }
    }
}
