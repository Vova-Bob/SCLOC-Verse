using System;

namespace SCLOCVerse.Models.Auth
{
    /// <summary>
    /// Результат аутентифікаційної операції.
    /// </summary>
    public abstract class AuthResult
    {
        private AuthResult()
        {
        }

        public sealed class Success : AuthResult
        {
            public Success(DiscordUserProfile profile)
            {
                Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            }

            public DiscordUserProfile Profile { get; }
        }

        public sealed class Failure : AuthResult
        {
            public Failure(string message)
            {
                Message = message ?? throw new ArgumentNullException(nameof(message));
            }

            public string Message { get; }
        }

        public sealed class Cancelled : AuthResult
        {
        }
    }
}
