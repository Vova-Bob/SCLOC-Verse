namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Безпечне сховище для збереження refresh token між запусками додатка.
    /// </summary>
    public interface ISecureSessionStorage
    {
        void SaveRefreshToken(string refreshToken);

        string? LoadRefreshToken();

        void DeleteRefreshToken();
    }
}
