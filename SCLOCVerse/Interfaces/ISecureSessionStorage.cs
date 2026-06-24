using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Безпечне сховище для збереження сесії між запусками додатка.
    /// </summary>
    public interface ISecureSessionStorage : IGotrueSessionPersistence<Session>
    {
        void SaveRefreshToken(string refreshToken);

        string? LoadRefreshToken();

        void DeleteRefreshToken();
    }
}
