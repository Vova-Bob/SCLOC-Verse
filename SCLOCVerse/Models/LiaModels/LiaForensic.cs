using Newtonsoft.Json;

namespace SCLOCVerse.Models.LiaModels
{
    /// <summary>
    /// Структурований forensic-контракт між PowerShell-інсталятором L.I.A та C#
    /// (контрольований JSON-протокол за маркером ##SCLOC_FORENSIC##, не аналіз тексту).
    /// </summary>
    public sealed class LiaForensic
    {
        [JsonProperty("hresult")]
        public string? Hresult { get; set; }

        [JsonProperty("phase")]
        public string? Phase { get; set; }

        [JsonProperty("message")]
        public string? Message { get; set; }

        [JsonProperty("installerType")]
        public string? InstallerType { get; set; }

        [JsonProperty("certificatePresent")]
        public bool? CertificatePresent { get; set; }

        [JsonProperty("certificateSubject")]
        public string? CertificateSubject { get; set; }

        [JsonProperty("certificateThumbprint")]
        public string? CertificateThumbprint { get; set; }

        [JsonProperty("activityId")]
        public string? ActivityId { get; set; }

        /// <summary>Сирий JSON-рядок (для ForensicJson у винятку).</summary>
        [JsonIgnore]
        public string? RawJson { get; set; }
    }
}