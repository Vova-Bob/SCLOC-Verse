using StarCitizenUA.Helpers;
using StarCitizenUA.Interfaces;
using StarCitizenUA.Models.ApplicationUpdate;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace StarCitizenUA.Services.ApplicationUpdate
{
    public class ApplicationUpdateService : IApplicationUpdateService
    {
        private readonly string _owner;
        private readonly string _repo;
        private readonly IApplicationVersionProvider _versionProvider;
        private readonly IUpdateChannelService _channelService;
        private readonly IGitHubReleaseClient _gitHubClient;
        private readonly IUpdateCacheService _cacheService;
        private readonly IReleaseChannelResolver _channelResolver;

        public ApplicationUpdateService(
            string owner,
            string repo,
            IApplicationVersionProvider versionProvider,
            IUpdateChannelService channelService,
            IGitHubReleaseClient gitHubClient,
            IUpdateCacheService cacheService,
            IReleaseChannelResolver channelResolver)
        {
            if (string.IsNullOrWhiteSpace(owner))
                throw new ArgumentException("Owner cannot be empty.", nameof(owner));
            if (string.IsNullOrWhiteSpace(repo))
                throw new ArgumentException("Repo cannot be empty.", nameof(repo));

            _owner = owner;
            _repo = repo;
            _versionProvider = versionProvider ?? throw new ArgumentNullException(nameof(versionProvider));
            _channelService = channelService ?? throw new ArgumentNullException(nameof(channelService));
            _gitHubClient = gitHubClient ?? throw new ArgumentNullException(nameof(gitHubClient));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _channelResolver = channelResolver ?? throw new ArgumentNullException(nameof(channelResolver));
        }

        public async Task<UpdateCheckResult> CheckForUpdatesAsync(
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var currentVersion = _versionProvider.GetCurrentVersion();
                var channel = ResolveChannel(_channelService.GetUpdateChannel());

                var releases = await GetReleasesAsync(channel, forceRefresh, cancellationToken).ConfigureAwait(false);

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

                var expectedChecksum = await ResolveExpectedChecksumAsync(
                    installerAsset,
                    latestRelease,
                    cancellationToken).ConfigureAwait(false);

                return CreateResult(
                    status,
                    isUpdateAvailable,
                    currentVersion,
                    latestVersion,
                    installerAsset.BrowserDownloadUrl,
                    expectedChecksum,
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
            bool forceRefresh,
            CancellationToken cancellationToken)
        {
            if (!forceRefresh)
            {
                var cachedEntry = await _cacheService.ReadAsync(channel).ConfigureAwait(false);

                if (cachedEntry != null && _cacheService.IsValid(cachedEntry, UpdateConstants.CacheTtl))
                {
                    return cachedEntry.Releases;
                }
            }

            var releases = await _gitHubClient.GetReleasesAsync(_owner, _repo, cancellationToken).ConfigureAwait(false);
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

        private async Task<string> ResolveExpectedChecksumAsync(
            GitHubReleaseAsset installerAsset,
            GitHubRelease release,
            CancellationToken cancellationToken)
        {
            var checksumAsset = release.Assets.FirstOrDefault(a =>
                a.Name.Equals($"{installerAsset.Name}.sha256", StringComparison.OrdinalIgnoreCase))
                ??
                release.Assets.FirstOrDefault(a =>
                    a.Name.Equals(
                        $"{Path.GetFileNameWithoutExtension(installerAsset.Name)}.sha256",
                        StringComparison.OrdinalIgnoreCase));

            if (checksumAsset == null)
                return string.Empty;

            try
            {
                var checksumContent = await _gitHubClient.DownloadTextAsync(
                    checksumAsset.BrowserDownloadUrl,
                    cancellationToken).ConfigureAwait(false);

                return ParseChecksum(checksumContent);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ParseChecksum(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return string.Empty;

            content = content.Trim();

            var tokens = content.Split(
                new[] { ' ', '\t', '\r', '\n', ':' },
                StringSplitOptions.RemoveEmptyEntries);

            foreach (var token in tokens)
            {
                if (token.Length >= 32 && token.All(c => Uri.IsHexDigit(c)))
                    return token;
            }

            return string.Empty;
        }

        private static UpdateCheckResult CreateResult(
            UpdateCheckStatus status,
            bool isUpdateAvailable,
            Version currentVersion,
            Version latestVersion,
            string downloadUrl,
            string expectedChecksum,
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
                ExpectedChecksum = expectedChecksum,
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
                ExpectedChecksum = string.Empty,
                ReleaseNotes = string.Empty,
                Channel = UpdateChannel.Stable,
                Message = message
            };
        }
    }
}
