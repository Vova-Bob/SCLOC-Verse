using SCLOCVerse.Models.Auth;
using System;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Read-only провайдер статусу аутентифікації для UI.
    /// </summary>
    public interface IAuthStatusProvider
    {
        AuthState State { get; }

        DiscordUserProfile? Profile { get; }

        event EventHandler? StatusChanged;
    }
}
