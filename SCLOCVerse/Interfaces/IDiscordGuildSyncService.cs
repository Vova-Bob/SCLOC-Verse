using Supabase.Gotrue;
using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Синхронізація списку Discord-гільдій користувача у таблицю
    /// public.user_discord_guilds. Працює best-effort: збій не повинен ламати логін.
    /// </summary>
    public interface IDiscordGuildSyncService
    {
        /// <summary>
        /// Синхронізує гільдії користувача з поточної сесії (потрібен scope "guilds").
        /// </summary>
        Task SyncUserGuildsAsync(Session session, CancellationToken cancellationToken = default);
    }
}
