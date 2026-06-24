using SCLOCVerse.Interfaces;
using SCLOCVerse.Services.Auth;
using Supabase;
using System;

namespace SCLOCVerse.Composition
{
    /// <summary>
    /// Композиція всіх залежностей аутентифікації.
    /// </summary>
    public sealed class AuthCompositionRoot
    {
        private readonly ISupabaseClientFactory _clientFactory;
        private readonly ISecureSessionStorage _secureStorage;
        private readonly ILoopbackCallbackListener _callbackListener;
        private readonly IInstallationService _installationService;
        private readonly IAuthService _authService;

        public AuthCompositionRoot(string supabaseUrl, string supabaseAnonKey)
        {
            _secureStorage = new SecureSessionStorage();
            _clientFactory = new SupabaseClientFactory(supabaseUrl, supabaseAnonKey, _secureStorage);
            _callbackListener = new LoopbackCallbackListener();
            _installationService = new InstallationService(_clientFactory);
            var guildSyncService = new DiscordGuildSyncService(_clientFactory);
            _authService = new AuthService(_clientFactory, _secureStorage, _callbackListener, _installationService, guildSyncService);
        }

        public IAuthService AuthService => _authService;

        public IAuthStatusProvider AuthStatusProvider => _authService;
    }
}
