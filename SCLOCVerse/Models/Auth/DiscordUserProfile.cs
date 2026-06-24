using System;

namespace SCLOCVerse.Models.Auth
{
    /// <summary>
    /// Профіль користувача Discord, який додаток відображає в UI.
    /// </summary>
    public sealed class DiscordUserProfile
    {
        public DiscordUserProfile(
            string id,
            string? username,
            string? globalName,
            string? avatarUrl,
            DateTime? joinedAt)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Username = username;
            GlobalName = globalName;
            AvatarUrl = avatarUrl;
            JoinedAt = joinedAt;
        }

        public string Id { get; }

        public string? Username { get; }

        public string? GlobalName { get; }

        public string? AvatarUrl { get; }

        /// <summary>
        /// Дата реєстрації акаунта (auth.users.created_at).
        /// </summary>
        public DateTime? JoinedAt { get; }

        /// <summary>
        /// Основне ім'я для UI: display name (global_name), або логін, або id.
        /// </summary>
        public string DisplayName => !string.IsNullOrWhiteSpace(GlobalName)
            ? GlobalName
            : (!string.IsNullOrWhiteSpace(Username) ? Username : Id);
    }
}
