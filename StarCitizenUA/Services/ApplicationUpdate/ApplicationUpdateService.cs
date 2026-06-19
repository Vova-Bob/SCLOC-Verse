using StarCitizenUA.Helpers;
using StarCitizenUA.Interfaces;
using StarCitizenUA.Models.ApplicationUpdate;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace StarCitizenUA.Services.ApplicationUpdate
{
    public class ApplicationUpdateService : IApplicationUpdateService
    {
        private readonly IApplicationVersionProvider _versionProvider;
        private readonly IUpdateChannelService _channelService;
        private readonly IGitHubReleaseClient _gitHubClient;
        private readonly IUpdateCacheService _cacheService;
        private readonly IReleaseChannelResolver _channelResolver;

        public ApplicationUpdateService(
            IApplicationVersionProvider versionProvider,
            IUpdateChannelService channelService,
            IGitHubReleaseClient gitHubClient,
            IUpdateCacheService cacheService,
            IReleaseChannelResolver channelResolver)
        {
            _versionProvider = versionProvider ?? throw new ArgumentNullException(nameof(versionProvider));
            _channelService = channelService ?? throw new ArgumentNullException(nameof(channelService));
            _gitHubClient = gitHubClient ?? throw new ArgumentNullException(nameof(gitHubClient));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _channelResolver = channelResolver ?? throw new ArgumentNullException(nameof(channelResolver));
        }

        public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var currentVersion = _versionProvider.GetCurrentVersion();
                var channel = ResolveChannel(_channelService.GetUpdateChannel());
                var owner = "SCLocalizationUA"; // placeholder until confirmed
                var repo = "SCLocalizationUA";  // placeholder until confirmed

                var releases = await GetReleasesAsync(channel, owner, repo, cancellationToken).ConfigureAwait(false);

                var filteredReleases = releases
                    .Where(r => _channelResolver.IsChannelMatch(r, channel))
                    .Where(r => VersionParser.TryParse(r.TagName, out _))
                    .OrderByDescending(r => VersionParser.Parse(r.TagName))
                    .ToList();

                GitHubRelease? latestRelease = null;
                GitHubReleaseAsset? installerAsset = null;

                foreach (var release in filteredReleases)
                {
                    var asset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                    if (asset == null)
                        continue;

                    latestRelease = release;
                    installerAsset = asset;
                    break;
                }

                if (latestRelease == null || installerAsset == null)
                {
                    return CreateResult(
                        UpdateCheckStatus.ChannelNotFound,
                        false,
                        currentVersion,
                        new Version(0, 0, 0, 0),
                        string.Empty,
                        string.Empty,
                        channel,
                        "Не знайдено релізів з інсталятором для поточного каналу.");
                }

                var latestVersion = VersionParser.Parse(latestRelease.TagName);
                var isUpdateAvailable = latestVersion > currentVersion;

                var status = isUpdateAvailable ? UpdateCheckStatus.UpdateAvailable : UpdateCheckStatus.UpToDate;
                var message = isUpdateAvailable
                    ? $"Доступна нова версія: {latestVersion}."
                    : "Ви використовуєте актуальну версію.";

                var assetInfo = new ReleaseAssetInfo
                {
                    Name = installerAsset.Name,
                    DownloadUrl = installerAsset.BrowserDownloadUrl,
                    Size = installerAsset.Size,
                    Checksum = ExtractChecksum(latestRelease.Body)
                };

                return CreateResult(
                    status,
                    isUpdateAvailable,
                    currentVersion,
                    latestVersion,
                    assetInfo.DownloadUrl,
                    latestRelease.Body,
                    channel,
                    message);
            }
            catch (HttpRequestException)
            {
                return CreateFailureResult("Помилка з'єднання з сервером оновлень.");
            }
            catch (ArgumentException)
            {
                return CreateFailureResult("Помилка обробки даних оновлення.");
            }
            catch
            {
                return CreateFailureResult("Невідома помилка під час перевірки оновлення.");
            }
        }

        private async Task<List<GitHubRelease>> GetReleasesAsync(
            UpdateChannel channel,
            string owner,
            string repo,
            CancellationToken cancellationToken)
        {
            var cachedEntry = await _cacheService.ReadAsync(channel).ConfigureAwait(false);

            if (cachedEntry != null && _cacheService.IsValid(cachedEntry, UpdateConstants.CacheTtl))
            {
                return cachedEntry.Releases;
            }

            var releases = await _gitHubClient.GetReleasesAsync(owner, repo, cancellationToken).ConfigureAwait(false);
            await _cacheService.WriteAsync(channel, releases).ConfigureAwait(false);

            return releases;
        }

        private static UpdateChannel ResolveChannel(string? channelName)
        {
            if (string.IsNullOrWhiteSpace(channelName))
                return UpdateChannel.Stable;

            return Enum.TryParse<UpdateChannel>(channelName, true, out var channel)
                ? channel
                : UpdateChannel.Stable;
        }

        private static string? ExtractChecksum(string? releaseBody)
        {
            if (string.IsNullOrWhiteSpace(releaseBody))
                return null;

            var lines = releaseBody.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("checksum:", StringComparison.OrdinalIgnoreCase))
                {
                    var checksum = trimmed.Substring("checksum:".Length).Trim();
                    return string.IsNullOrWhiteSpace(checksum) ? null : checksum;
                }
            }

            return null;
        }

        private static UpdateCheckResult CreateResult(
            UpdateCheckStatus status,
            bool isUpdateAvailable,
            Version currentVersion,
            Version latestVersion,
            string downloadUrl,
            string releaseNotes,
            UpdateChannel channel,
            string message)
        {
            return new UpdateCheckResult
            {
                Status = status,
                IsUpdateAvailable = isUpdateAvailable,
                CurrentVersion = currentVersion,
                LatestVersion = latestVersion,
                DownloadUrl = downloadUrl,
                ReleaseNotes = releaseNotes,
                Channel = channel,
                Message = message
            };
        }

        private static UpdateCheckResult CreateFailureResult(string message)
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.CheckFailed,
                IsUpdateAvailable = false,
                CurrentVersion = new Version(0, 0, 0, 0),
                LatestVersion = new Version(0, 0, 0, 0),
                DownloadUrl = string.Empty,
                ReleaseNotes = string.Empty,
                Channel = UpdateChannel.Stable,
                Message = message
            };
        }
    }
}
