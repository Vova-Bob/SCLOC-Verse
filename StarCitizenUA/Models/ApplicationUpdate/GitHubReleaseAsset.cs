using Newtonsoft.Json;

namespace StarCitizenUA.Models.ApplicationUpdate
{
    public class GitHubReleaseAsset
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;

        [JsonProperty("size")]
        public long Size { get; set; }
    }
}
