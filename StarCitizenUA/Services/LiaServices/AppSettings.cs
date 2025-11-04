using Newtonsoft.Json.Linq;
using System.Net.Http;

namespace StarCitizenUA.Services.LiaServices
{
    public class AppSettings
    {
        public static string BaseUrl = "https://raw.githubusercontent.com/AlexLiberty/voiceattack-updater/master/Star%20Citizen%20Voice%20Pack%20LIA/";

        public static string VersionUrl = "https://raw.githubusercontent.com/AlexLiberty/voiceattack-updater/master/";

        public static string VoskModelUrl = "https://alphacephei.com/vosk/models/vosk-model-uk-v3.zip";

        public static string VoskModelFileName = "vosk-model-uk-v3.zip";

        public static string VoskTargetFolderName = "model-ua";
        public static string VersionFile => $"{VersionUrl}version.txt";

        public static string GitHubReleasesUrl = "https://api.github.com/repos/AlexLiberty/StarCitizen_VoicePack_Updater_Releases/releases/latest";

        public static async Task<string> GetLatestReleaseVersionAsync()
        {
            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "LIA Voice Pack Updater");

            try
            {
                var response = await client.GetStringAsync(GitHubReleasesUrl);
                var releaseInfo = JObject.Parse(response);
                var version = releaseInfo["tag_name"]?.ToString();

                return version?.Trim()?? "невідомо";
            }
            catch
            {
                return "невідомо";
            }
        }

        public static async Task<string> GetDownloadUrlAsync()
        {
            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "LIA Voice Pack Updater");

            try
            {
                var response = await client.GetStringAsync(GitHubReleasesUrl);
                var releaseInfo = JObject.Parse(response);
                var downloadUrl = releaseInfo["assets"]?[0]?["browser_download_url"]?.ToString();

                return downloadUrl ?? "";
            }
            catch
            {
                return string.Empty;
            }
        }

        public static async Task<string> GetVoicePackVersionAsync()
        {
            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "LIA Voice Pack Updater");

            try
            {
                string content = await client.GetStringAsync(VersionFile);
                string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    if (line.StartsWith("#version=", StringComparison.OrdinalIgnoreCase) || line.StartsWith("version=", StringComparison.OrdinalIgnoreCase))
                    {
                        return line.Replace("version=", "").Replace("#", "").Trim();
                    }
                }

                return "невідомо";
            }
            catch
            {
                return "невідомо";
            }
        }
    }
}