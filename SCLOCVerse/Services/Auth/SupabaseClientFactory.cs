using SCLOCVerse.Interfaces;
using Supabase;
using System;

namespace SCLOCVerse.Services.Auth
{
    /// <summary>
    /// Створює єдиний спільний Supabase клієнт для всіх сервісів аутентифікації.
    /// </summary>
    public sealed class SupabaseClientFactory : ISupabaseClientFactory, IDisposable
    {
        private readonly string _url;
        private readonly string _anonKey;
        private readonly ISecureSessionStorage _sessionStorage;
        private Client? _client;
        private readonly object _lock = new();
        private bool _disposed;

        public SupabaseClientFactory(string url, string anonKey, ISecureSessionStorage sessionStorage)
        {
            _url = url ?? throw new ArgumentNullException(nameof(url));
            _anonKey = anonKey ?? throw new ArgumentNullException(nameof(anonKey));
            _sessionStorage = sessionStorage ?? throw new ArgumentNullException(nameof(sessionStorage));
        }

        public Client CreateClient()
        {
            if (_client != null)
                return _client;

            lock (_lock)
            {
                if (_client != null)
                    return _client;

                var options = new SupabaseOptions
                {
                    AutoRefreshToken = true,
                    AutoConnectRealtime = false,
                    SessionHandler = _sessionStorage
                };

                _client = new Client(_url, _anonKey, options);
                _client.Auth.SetPersistence(_sessionStorage);
                return _client;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            Client? client;
            lock (_lock)
            {
                client = _client;
                _client = null;
            }

            client?.Auth.Shutdown();
        }
    }
}
