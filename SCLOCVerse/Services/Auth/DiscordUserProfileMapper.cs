using SCLOCVerse.Models.Auth;
using Supabase.Gotrue;
using System;
using System.Collections.Generic;
using System.Text.Json;
using Newtonsoft.Json.Linq;

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

            // Display name Discord кладе у вкладений custom_claims.global_name (напр. "VALDEUS | Вова").
            var globalName = GetNestedMetadata(user, "custom_claims", "global_name");

            // Логін/username — лише як fallback, у UI не відображається.
            var username = GetMetadata(user, "full_name") ?? GetMetadata(user, "name");
            var avatarUrl = GetMetadata(user, "avatar_url") ?? GetMetadata(user, "picture");

            return new DiscordUserProfile(id, username, globalName, avatarUrl, user.CreatedAt);
        }

        private static string? GetMetadata(User user, string key)
        {
            if (user.UserMetadata == null)
                return null;

            if (!user.UserMetadata.TryGetValue(key, out var value) || value == null)
                return null;

            return value switch
            {
                JsonElement el => el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString(),
                string s => s,
                _ => value.ToString()
            };
        }

        /// <summary>
        /// Читає вкладене значення metadata[parentKey][childKey], стійке до типу
        /// вкладеного об'єкта (Newtonsoft JObject, System.Text.Json JsonElement, словник).
        /// </summary>
        private static string? GetNestedMetadata(User user, string parentKey, string childKey)
        {
            if (user.UserMetadata == null)
                return null;

            if (!user.UserMetadata.TryGetValue(parentKey, out var parent) || parent == null)
                return null;

            return parent switch
            {
                JObject jo => jo.Value<string>(childKey),
                JsonElement el when el.ValueKind == JsonValueKind.Object
                    => el.TryGetProperty(childKey, out var child) ? GetStringFromElement(child) : null,
                IDictionary<string, object?> dict
                    => dict.TryGetValue(childKey, out var v) ? v?.ToString() : null,
                _ => null
            };
        }

        private static string? GetStringFromElement(JsonElement el)
            => el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }
}
