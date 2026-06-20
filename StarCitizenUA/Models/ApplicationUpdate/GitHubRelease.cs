using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace StarCitizenUA.Models.ApplicationUpdate
{
    public class GitHubRelease
    {
        [JsonProperty("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("body")]
        public string Body { get; set; } = string.Empty;

        [JsonProperty("published_at")]
        public DateTimeOffset PublishedAt { get; set; }

        [JsonProperty("prerelease")]
        public bool Prerelease { get; set; }

        [JsonProperty("assets")]
        public List<GitHubReleaseAsset> Assets { get; set; } = new();
    }
}
