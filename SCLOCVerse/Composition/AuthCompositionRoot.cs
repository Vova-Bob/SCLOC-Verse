using SCLOCVerse.Interfaces;
using SCLOCVerse.Services.Auth;
using Supabase;
using System;

namespace SCLOCVerse.Composition
{
    /// <summary>
    /// Композиція всіх залежностей аутентифікації.
    /// </summary>
    public sealed class AuthCompositionRoot : IDisposable
    {
        private readonly ISupabaseClientFactory _clientFactory;
        private readonly ISecureSessionStorage _secureStorage;
        private readonly ILoopbackCallbackListener _callbackListener;
        private readonly IInstallationService _installationService;
        private readonly ISessionTrackerService _sessionTrackerService;
        private readonly IAuthService _authService;

        public AuthCompositionRoot(string supabaseUrl, string supabaseAnonKey)
        {
            _secureStorage = new SecureSessionStorage();
            _clientFactory = new SupabaseClientFactory(supabaseUrl, supabaseAnonKey, _secureStorage);
            _callbackListener = new LoopbackCallbackListener();
            _installationService = new InstallationService(_clientFactory);
            var guildSyncService = new DiscordGuildSyncService(_clientFactory);
            _sessionTrackerService = new SessionTrackerService(_clientFactory, _installationService);
            _authService = new AuthService(_clientFactory, _secureStorage, _callbackListener, _installationService, guildSyncService, _sessionTrackerService);
        }

        public IAuthService AuthService => _authService;

        public IAuthStatusProvider AuthStatusProvider => _authService;

        public ISessionTrackerService SessionTrackerService => _sessionTrackerService;

        public void Dispose()
        {
            if (_authService is IDisposable disposable)
                disposable.Dispose();

            // SupabaseClientFactory тримає єдиний Supabase.Client —
            // диспозим його окремо, щоб зупинити TokenRefresh timer.
            if (_clientFactory is IDisposable factoryDisposable)
                factoryDisposable.Dispose();
        }
    }
}
