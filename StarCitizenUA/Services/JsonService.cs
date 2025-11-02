using Newtonsoft.Json;
using System.IO;

namespace StarCitizenUA.Services
{
    public class JsonService
    {
        private readonly string _filePath;

        public JsonService(string filePath)
        {
            _filePath = filePath;
        }

        public ReadmeData LoadReadme()
        {
            if (!File.Exists(_filePath))
            {
                return new ReadmeData { ReadmeText = "Файл readme.json не знайдено." };
            }

            string json = File.ReadAllText(_filePath);
            return JsonConvert.DeserializeObject<ReadmeData>(json) ?? new ReadmeData { ReadmeText = "Помилка при читанні JSON." };
        }
    }
}
