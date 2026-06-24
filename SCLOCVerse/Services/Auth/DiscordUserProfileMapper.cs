using SCLOCVerse.Models.Auth;
using Supabase.Gotrue;
using System;

namespace SCLOCVerse.Services.Auth
{
    /// <summary>
    /// Мапить Supabase User/Identity до DiscordUserProfile.
    /// </summary>
    public static class DiscordUserProfileMapper
    {
        public static DiscordUserProfile Map(User user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            var id = user.Id?.ToString() ?? string.Empty;
            var username = GetMetadata(user, "user_name") ?? GetMetadata(user, "name");
            var globalName = GetMetadata(user, "full_name");
            var avatarUrl = GetMetadata(user, "avatar_url");

            return new DiscordUserProfile(id, username, globalName, avatarUrl);
        }

        private static string? GetMetadata(User user, string key)
        {
            if (user.UserMetadata == null)
                return null;

            return user.UserMetadata.ContainsKey(key) ? user.UserMetadata[key]?.ToString() : null;
        }
    }
}
