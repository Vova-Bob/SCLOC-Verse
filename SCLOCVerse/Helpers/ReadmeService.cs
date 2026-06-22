using SCLOCVerse.Interfaces;
using System.IO;
using System.Windows.Documents;

namespace SCLOCVerse.Helpers
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
            window.TxtSelectedPath.Text = readmeData.TxtSelectedPath;
            window.DefaultPathText = readmeData.DefaultPathText;
            window.MissingGameFolderToastText = readmeData.MissingGameFolderToast;
        }
        private void SetAllFieldsMissing(MainWindow window, string message)
        {
            window.TxtReadme.Text = message;
            window.TxtLocalizationReadme.Text = message;
            window.TxtLiaReadme.Document.Blocks.Add(new Paragraph(new Run(message)));
            window.TxtSelectedPath.Text = string.Empty;
        }
    }
}
