using SCLOCVerse.Models.Auth;
using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Головний фасад аутентифікації Discord OAuth через Supabase.
    /// </summary>
    public interface IAuthService : IAuthStatusProvider
    {
        /// <summary>
        /// Запускає OAuth flow: відкриває браузер, очікує callback та створює сесію.
        /// </summary>
        Task<AuthResult> SignInAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Завантажує збережену сесію при старті додатка.
        /// </summary>
        Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Завершує сесію локально та на сервері.
        /// </summary>
        Task SignOutAsync(CancellationToken cancellationToken = default);
    }
}
