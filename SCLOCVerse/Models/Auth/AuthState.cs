namespace SCLOCVerse.Models.Auth
{
    /// <summary>
    /// Поточний стан аутентифікації в додатку.
    /// </summary>
    public enum AuthState
    {
        SignedOut,
        SigningIn,
        SignedIn,
        Error
    }
}
