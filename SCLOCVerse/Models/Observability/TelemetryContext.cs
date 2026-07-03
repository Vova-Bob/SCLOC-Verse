using System.Collections.Generic;

namespace SCLOCVerse.Models.Observability
{
    /// <summary>
    /// Додатковий контекст події (опціональний). Для щасливих подій (Start/Success)
    /// передається null; для невдач — несе diagnostic-сигнали (Конституція, Стаття 13:
    /// Failed обов'язково має signal).
    /// </summary>
    public sealed class TelemetryContext
    {
        /// <summary>Перевизначення severity (інакше виводиться з outcome).</summary>
        public string? Severity { get; set; }

        /// <summary>Перевизначення категорії (Critical/Operational/Diagnostic/Analytics).</summary>
        public string? Category { get; set; }

        /// <summary>Джерело помилки: Supabase/GitHub/PowerShell/HttpClient/CLR.</summary>
        public string? Source { get; set; }

        public int? HttpStatus { get; set; }

        /// <summary>Шістнадцяткове представлення, напр. "0x800B0109".</summary>
        public string? Hresult { get; set; }

        /// <summary>Postgrest/Postgres код, напр. "42501".</summary>
        public string? SupabaseCode { get; set; }

        public string? ExceptionType { get; set; }

        /// <summary>Коротке, санітизоване повідомлення (НЕ повний stack).</summary>
        public string? ErrorMessage { get; set; }

        public int? DurationMs { get; set; }

        /// <summary>Структуровані деталі (напр. sanitized stack-trace для Crash).</summary>
        public Dictionary<string, object?>? Detail { get; set; }
    }
}
