using Newtonsoft.Json;
using SCLOCVerse.Models.LiaModels;
using System;

namespace SCLOCVerse.Services.LiaServices
{
    /// <summary>
    /// Парсить службовий forensic-блок з output PowerShell-інсталятора L.I.A.
    /// Контрольований контракт (маркер ##SCLOC_FORENSIC## + наступний JSON-рядок),
    /// не аналіз тексту повідомлення помилки.
    /// </summary>
    public static class LiaForensicParser
    {
        private const string Marker = "##SCLOC_FORENSIC##";

        public static LiaForensic? TryParse(string? output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return null;

            try
            {
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < lines.Length - 1; i++)
                {
                    if (!string.Equals(lines[i].Trim(), Marker, StringComparison.Ordinal))
                        continue;

                    var jsonLine = lines[i + 1].Trim();
                    if (string.IsNullOrWhiteSpace(jsonLine))
                        return null;

                    var forensic = JsonConvert.DeserializeObject<LiaForensic>(jsonLine);
                    if (forensic != null)
                        forensic.RawJson = jsonLine;
                    return forensic;
                }
            }
            catch
            {
                // best-effort: контракт порушено — повертаємо null, виняток буде InvalidOperationException.
            }

            return null;
        }
    }
}