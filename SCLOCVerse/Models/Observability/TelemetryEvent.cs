using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System;

namespace SCLOCVerse.Models.Observability
{
    /// <summary>
    /// Модель таблиці public.telemetry_events — єдиного append-only журналу
    /// SCLOC Observability Platform (Конституція, Стаття 5).
    /// </summary>
    [Table("telemetry_events")]
    public class TelemetryEvent : BaseModel
    {
        [PrimaryKey("id", false)]
        public Guid Id { get; set; }

        [Column("client_event_id")]
        public Guid ClientEventId { get; set; }

        [Column("session_id")]
        public Guid SessionId { get; set; }

        [Column("correlation_id")]
        public Guid CorrelationId { get; set; }

        [Column("step")]
        public int Step { get; set; }

        [Column("install_id")]
        public string InstallId { get; set; } = string.Empty;

        [Column("user_id")]
        public Guid? UserId { get; set; }

        [Column("occurred_at")]
        public DateTimeOffset OccurredAt { get; set; }

        // received_at має DEFAULT now() на сервері й NOT NULL.
        // Навмисно НЕ мапимо в клієнтській моделі: якщо відправити null,
        // Postgres відхилить вставку в NOT NULL-колонку. Сервер ставить now()
        // сам; читання йде через VIEW напряму з таблиці.

        [Column("app_version")]
        public string AppVersion { get; set; } = string.Empty;

        [Column("git_commit")]
        public string? GitCommit { get; set; }

        [Column("channel")]
        public string Channel { get; set; } = "stable";

        [Column("telemetry_version")]
        public int TelemetryVersion { get; set; } = 1;

        [Column("os_version")]
        public string? OsVersion { get; set; }

        [Column("country")]
        public string? Country { get; set; }

        [Column("component")]
        public string Component { get; set; } = string.Empty;

        [Column("operation")]
        public string Operation { get; set; } = string.Empty;

        [Column("outcome")]
        public string Outcome { get; set; } = string.Empty;

        [Column("severity")]
        public string Severity { get; set; } = "Info";

        [Column("category")]
        public string Category { get; set; } = "Operational";

        [Column("source")]
        public string? Source { get; set; }

        [Column("http_status")]
        public int? HttpStatus { get; set; }

        [Column("hresult")]
        public string? Hresult { get; set; }

        [Column("supabase_code")]
        public string? SupabaseCode { get; set; }

        [Column("exception_type")]
        public string? ExceptionType { get; set; }

        [Column("error_message")]
        public string? ErrorMessage { get; set; }

        [Column("duration_ms")]
        public int? DurationMs { get; set; }

        [Column("detail")]
        public System.Collections.Generic.Dictionary<string, object?>? Detail { get; set; }
    }
}
