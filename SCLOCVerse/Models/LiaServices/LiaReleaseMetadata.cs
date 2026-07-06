using System.Text.Json.Serialization;

namespace SCLOCVerse.Models.LiaServices
{
    /// <summary>
    /// Метадані кешу L.I.A release-check (ETag + Last-Modified для Conditional GET).
    /// Ідентичний патерн до LocalizationMetadata — зберігається у
    /// %LOCALAPPDATA%\SCLOCVerse\cache\lia.meta.json.
    /// </summary>
    public sealed class LiaReleaseMetadata
    {
        /// <summary>ETag останнього успішного запиту до /releases/latest.</summary>
        [JsonPropertyName("etag")]
        public string? ETag { get; set; }

        /// <summary>Last-Modified останнього успішного запиту.</summary>
        [JsonPropertyName("lastModified")]
        public DateTimeOffset? LastModified { get; set; }

        /// <summary>Версія останнього відомого релізу (для швидкої перевірки без парсингу JSON).</summary>
        [JsonPropertyName("lastKnownVersion")]
        public string? LastKnownVersion { get; set; }
    }
}