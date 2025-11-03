using StarCitizenUA.Interfaces;
using StarCitizenUA.Views;
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
                window.TxtReadme.Text = "Readme файл не знайдено.";
                window.TxtSelectedPath.Text = string.Empty;
                return;
            }

            var jsonService = new JsonService(jsonPath);
            var readmeData = jsonService.LoadReadme();

            window.TxtReadme.Text = readmeData.ReadmeText;
            window.TxtSelectedPath.Text = readmeData.TxtSelectedPath;
            window.DefaultPathText = readmeData.DefaultPathText;
            window.MissingGameFolderToastText = readmeData.MissingGameFolderToast;
            window.MissingVoiceAttackFolderToastText = readmeData.MissingVoiceAttackFolderToast;
        }
    }
}