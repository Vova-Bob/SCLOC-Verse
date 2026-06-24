using Supabase.Postgrest.Models;
using System;

namespace SCLOCVerse.Models.Auth
{
    /// <summary>
    /// Модель таблиці public.sessions: один запуск додатка = одна сесія.
    /// </summary>
    [Supabase.Postgrest.Attributes.Table("sessions")]
    public class AppSession : BaseModel
    {
        [Supabase.Postgrest.Attributes.PrimaryKey("id", false)]
        public Guid Id { get; set; }

        [Supabase.Postgrest.Attributes.Column("user_id")]
        public Guid? UserId { get; set; }

        [Supabase.Postgrest.Attributes.Column("install_id")]
        public string InstallId { get; set; } = string.Empty;

        [Supabase.Postgrest.Attributes.Column("started_at")]
        public DateTimeOffset StartedAt { get; set; }

        [Supabase.Postgrest.Attributes.Column("ended_at")]
        public DateTimeOffset? EndedAt { get; set; }

        [Supabase.Postgrest.Attributes.Column("app_version")]
        public string? AppVersion { get; set; }

        [Supabase.Postgrest.Attributes.Column("os_version")]
        public string? OsVersion { get; set; }
    }
}
