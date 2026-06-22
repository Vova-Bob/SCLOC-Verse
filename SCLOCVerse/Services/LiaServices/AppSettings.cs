using System.IO;

namespace SCLOCVerse.Services.LiaServices
{
    public class AppSettings
    {
        public const string GitHubReleasesUrl = "https://api.github.com/repos/AlexLiberty/StarCitizen_VoicePack_Releases/releases/latest";
        public const string PackageName = "a7940cff-f42b-43cd-a559-d7bc52e030cc";
        public const string AppId = "App";

        public static string UpdatesDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "L.I.A Voice Assistant Installer");
    }
}