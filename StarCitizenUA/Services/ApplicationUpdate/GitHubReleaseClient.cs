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

        public GitHubReleaseClient(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<GitHubRelease?> GetLatestReleaseAsync(string owner, string repo, string userAgent, CancellationToken cancellationToken = default)
        {
            var releases = await GetReleasesAsync(owner, repo, userAgent, cancellationToken).ConfigureAwait(false);
            return releases.Count > 0 ? releases[0] : null;
        }

        public async Task<List<GitHubRelease>> GetReleasesAsync(string owner, string repo, string userAgent, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(owner))
                throw new ArgumentException("Owner cannot be empty.", nameof(owner));
            if (string.IsNullOrWhiteSpace(repo))
                throw new ArgumentException("Repo cannot be empty.", nameof(repo));
            if (string.IsNullOrWhiteSpace(userAgent))
                throw new ArgumentException("User-Agent cannot be empty.", nameof(userAgent));

            var url = $"https://api.github.com/repos/{owner}/{repo}/releases";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", userAgent);
            request.Headers.Add("Accept", "application/vnd.github+json");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return JsonConvert.DeserializeObject<List<GitHubRelease>>(json) ?? new List<GitHubRelease>();
        }
    }
}
