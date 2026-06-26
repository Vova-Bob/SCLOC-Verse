namespace SCLOCVerse.Models.Auth
{
    /// <summary>
    /// Поточний стан аутентифікації в додатку.
    /// </summary>
    public enum AuthState
    {
        Checking,
        SignedOut,
        SigningIn,
        SignedIn,
        Error
    }
}
