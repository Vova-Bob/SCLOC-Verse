using SCLOCVerse.Interfaces;
using Supabase;
using System;

namespace SCLOCVerse.Services.Auth
{
    /// <summary>
    /// Створює налаштований Supabase клієнт з публічним anon key.
    /// </summary>
    public sealed class SupabaseClientFactory : ISupabaseClientFactory
    {
        private readonly string _url;
        private readonly string _anonKey;

        public SupabaseClientFactory(string url, string anonKey)
        {
            _url = url ?? throw new ArgumentNullException(nameof(url));
            _anonKey = anonKey ?? throw new ArgumentNullException(nameof(anonKey));
        }

        public Client CreateClient()
        {
            var options = new SupabaseOptions
            {
                AutoRefreshToken = true,
                AutoConnectRealtime = false
            };

            return new Client(_url, _anonKey, options);
        }
    }
}
