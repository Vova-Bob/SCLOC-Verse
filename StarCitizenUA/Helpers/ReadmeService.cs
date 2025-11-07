using StarCitizenUA.Interfaces;
using System.IO;

namespace StarCitizenUA.Helpers
{
    public class ReadmeService : IReadmeService
    {
        private readonly string _jsonFileName = "PathText.json";

        public void LoadReadme(MainWindow window)
        {
            if (window == null) throw new ArgumentNullException(nameof(window));

            string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _jsonFileName);
            if (!File.Exists(jsonPath))
            {
                SetAllFieldsMissing(window, "PathText не знайдено.");
                return;
            }

            var jsonService = new JsonService(jsonPath);
            var readmeData = jsonService.LoadReadme();

            window.TxtReadme.Text = readmeData.ReadmeText;
            window.TxtLocalizationReadme.Text = readmeData.LocalizationReadmeText;
            window.TxtLiaReadme.Text = readmeData.LiaReadmeText;
            window.TxtLiaSettingsReadme.Text = readmeData.LiaSettingsReadmeText;
            window.TxtSelectedPath.Text = readmeData.TxtSelectedPath;
            window.DefaultPathText = readmeData.DefaultPathText;
            window.MissingGameFolderToastText = readmeData.MissingGameFolderToast;
            window.MissingVoiceAttackFolderToastText = readmeData.MissingVoiceAttackFolderToast;
        }
        private void SetAllFieldsMissing(MainWindow window, string message)
        {
            window.TxtReadme.Text = message;
            window.TxtLocalizationReadme.Text = message;
            window.TxtLiaReadme.Text = message;
            window.TxtLiaSettingsReadme.Text = message;
            window.TxtSelectedPath.Text = string.Empty;
        }
    }
}