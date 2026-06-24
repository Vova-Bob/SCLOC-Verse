using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System;

namespace SCLOCVerse.Models.Auth
{
    /// <summary>
    /// Модель таблиці public.user_discord_guilds.
    /// Зберігає лише id + назву Discord-сервера користувача для читабельної
    /// статистики в адмін-панелі (без додаткових запитів до Discord API).
    /// </summary>
    [Table("user_discord_guilds")]
    public class UserDiscordGuild : BaseModel
    {
        [PrimaryKey("id", false)]
        public Guid Id { get; set; }

        [Column("user_id")]
        public Guid? UserId { get; set; }

        [Column("discord_guild_id")]
        public string DiscordGuildId { get; set; } = string.Empty;

        [Column("guild_name")]
        public string? GuildName { get; set; }

        [Column("synced_at")]
        public DateTimeOffset? SyncedAt { get; set; }
    }
}
