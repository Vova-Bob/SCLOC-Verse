namespace SCLOCVerse.Models.Auth
{
    /// <summary>
    /// Профіль користувача Discord, який додаток відображає в UI.
    /// </summary>
    public sealed class DiscordUserProfile
    {
        public DiscordUserProfile(string id, string? username, string? globalName, string? avatarUrl)
        {
            Id = id ?? throw new System.ArgumentNullException(nameof(id));
            Username = username;
            GlobalName = globalName;
            AvatarUrl = avatarUrl;
        }

        public string Id { get; }

        public string? Username { get; }

        public string? GlobalName { get; }

        public string? AvatarUrl { get; }

        public string DisplayName => !string.IsNullOrWhiteSpace(GlobalName)
            ? GlobalName
            : (!string.IsNullOrWhiteSpace(Username) ? Username : Id);
    }
}
