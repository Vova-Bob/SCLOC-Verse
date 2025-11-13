using StarCitizenUA.Interfaces;
using System.IO;
using System.Windows.Documents;

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
            window.TxtLiaReadme.Document.Blocks.Add(new Paragraph(new Run(readmeData.LiaReadmeText)));
            window.TxtLiaSettingsReadme.Text = readmeData.LiaSettingsReadmeText;
            window.TxtSelectedPath.Text = readmeData.TxtSelectedPath;
            window.DefaultPathText = readmeData.DefaultPathText;
            window.TxtSelectedLiaPath.Text = readmeData.TxtSelectedLiaPath;
            window.DefaultPathLiaText = readmeData.DefaultPathLiaText;
            window.MissingGameFolderToastText = readmeData.MissingGameFolderToast;
            window.MissingVoiceAttackFolderToastText = readmeData.MissingVoiceAttackFolderToast;
            window.TxtVoiceAttackReadme.Text = readmeData.VoiceAttackReadmeText;
        }
        private void SetAllFieldsMissing(MainWindow window, string message)
        {
            window.TxtReadme.Text = message;
            window.TxtLocalizationReadme.Text = message;
            window.TxtLiaReadme.Document.Blocks.Add(new Paragraph(new Run(message)));
            window.TxtLiaSettingsReadme.Text = message;
            window.TxtSelectedPath.Text = string.Empty;
            window.TxtVoiceAttackReadme.Text = message;
        }
    }
}