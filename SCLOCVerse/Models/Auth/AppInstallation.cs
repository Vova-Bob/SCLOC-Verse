using Supabase.Postgrest.Models;
using System;

namespace SCLOCVerse.Models.Auth
{
    /// <summary>
    /// Модель таблиці public.app_installations для зв'язку User → Installations.
    /// </summary>
    [Supabase.Postgrest.Attributes.Table("app_installations")]
    public class AppInstallation : BaseModel
    {
        [Supabase.Postgrest.Attributes.PrimaryKey("id", false)]
        public Guid Id { get; set; }

        [Supabase.Postgrest.Attributes.Column("user_id")]
        public Guid? UserId { get; set; }

        [Supabase.Postgrest.Attributes.Column("install_id")]
        public string InstallId { get; set; } = string.Empty;

        [Supabase.Postgrest.Attributes.Column("app_version")]
        public string? AppVersion { get; set; }

        [Supabase.Postgrest.Attributes.Column("platform")]
        public string? Platform { get; set; }

        [Supabase.Postgrest.Attributes.Column("machine_id")]
        public string? MachineId { get; set; }

        [Supabase.Postgrest.Attributes.Column("os_version")]
        public string? OsVersion { get; set; }

        [Supabase.Postgrest.Attributes.Column("last_seen")]
        public DateTimeOffset? LastSeen { get; set; }

        [Supabase.Postgrest.Attributes.Column("first_seen")]
        public DateTimeOffset? FirstSeen { get; set; }

        [Supabase.Postgrest.Attributes.Column("created_at")]
        public DateTimeOffset CreatedAt { get; set; }

        [Supabase.Postgrest.Attributes.Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Supabase.Postgrest.Attributes.Column("updated_at")]
        public DateTimeOffset? UpdatedAt { get; set; }

        [Supabase.Postgrest.Attributes.Column("last_session_at")]
        public DateTimeOffset? LastSessionAt { get; set; }
    }
}
