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
    /// <remarks>
    /// Два методи парсингу:
    /// • <see cref="TryParse"/> — повний forensic з маркером (існуючий non-elevated шлях).
    /// • <see cref="TryParseMinimal"/> — мінімальний пакет JSON без маркера (новий elevated шлях,
    ///   де PowerShell передає лише {phase, hresult, activityId, message}, а C# добудовує решту).
    /// Обидва повертають той самий <see cref="LiaForensic"/> — перехідний період,旧 контракт
    /// не видаляється до стабілізації нового.
    /// </remarks>
    public static class LiaForensicParser
    {
        private const string Marker = "##SCLOC_FORENSIC##";

        /// <summary>
        /// Парсить повний forensic-блок з маркером ##SCLOC_FORENSIC## (існуючий non-elevated шлях).
        /// </summary>
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

        /// <summary>
        /// Парсить мінімальний forensic-пакет (чистий JSON без маркера) від elevated PowerShell.
        /// На відміну від <see cref="TryParse"/>, очікує валідний JSON-рядок напряму,
        /// а не текст з маркером ##SCLOC_FORENSIC##.
        /// </summary>
        /// <param name="json">
        /// Мінімальний пакет: <c>{ "phase": "...", "hresult": "0x...", "activityId": "...", "message": "..." }</c>.
        /// Усі поля optional — C# добудовує сертифікат/InstallerType/AppxLog самостійно.
        /// </param>
        public static LiaForensic? TryParseMinimal(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var forensic = JsonConvert.DeserializeObject<LiaForensic>(json);
                if (forensic != null)
                    forensic.RawJson = json;
                return forensic;
            }
            catch
            {
                // best-effort: JSON пошкоджено — повертаємо null, caller робить fallback.
                return null;
            }
        }
    }
}