using Newtonsoft.Json;
using StarCitizenUA.Interfaces;
using StarCitizenUA.Models.ApplicationUpdate;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace StarCitizenUA.Services.ApplicationUpdate
{
    public class GitHubReleaseClient : IGitHubReleaseClient
    {
        private readonly HttpClient _httpClient;

        public GitHubReleaseClient(HttpClient httpClient, string userAgent)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

            if (string.IsNullOrWhiteSpace(userAgent))
                throw new ArgumentException("User-Agent cannot be empty.", nameof(userAgent));

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }

        public async Task<GitHubRelease?> GetLatestReleaseAsync(
            string owner,
            string repo,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(owner))
                throw new ArgumentException("Owner cannot be empty.", nameof(owner));
            if (string.IsNullOrWhiteSpace(repo))
                throw new ArgumentException("Repo cannot be empty.", nameof(repo));

            var url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";

            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return JsonConvert.DeserializeObject<GitHubRelease>(json);
        }

        public async Task<List<GitHubRelease>> GetReleasesAsync(
            string owner,
            string repo,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(owner))
                throw new ArgumentException("Owner cannot be empty.", nameof(owner));
            if (string.IsNullOrWhiteSpace(repo))
                throw new ArgumentException("Repo cannot be empty.", nameof(repo));

            var url = $"https://api.github.com/repos/{owner}/{repo}/releases";

            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return JsonConvert.DeserializeObject<List<GitHubRelease>>(json) ?? new List<GitHubRelease>();
        }
    }
}
