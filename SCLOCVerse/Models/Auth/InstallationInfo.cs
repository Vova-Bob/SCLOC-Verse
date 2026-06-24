using Supabase.Postgrest.Models;
using System;

namespace SCLOCVerse.Models.Auth
{
    /// <summary>
    /// Модель таблиці public.installations для зв'язку User → Installations.
    /// </summary>
    [Supabase.Postgrest.Attributes.Table("installations")]
    public class InstallationInfo : BaseModel
    {
        [Supabase.Postgrest.Attributes.PrimaryKey("id", false)]
        public Guid Id { get; set; }

        [Supabase.Postgrest.Attributes.Column("user_id")]
        public Guid UserId { get; set; }

        [Supabase.Postgrest.Attributes.Column("install_id")]
        public string InstallId { get; set; } = string.Empty;

        [Supabase.Postgrest.Attributes.Column("device_name")]
        public string? DeviceName { get; set; }

        [Supabase.Postgrest.Attributes.Column("last_seen_at")]
        public DateTimeOffset LastSeenAt { get; set; }

        [Supabase.Postgrest.Attributes.Column("created_at")]
        public DateTimeOffset CreatedAt { get; set; }
    }
}
