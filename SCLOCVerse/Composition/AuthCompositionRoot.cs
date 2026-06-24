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
            _clientFactory = new SupabaseClientFactory(supabaseUrl, supabaseAnonKey);
            _secureStorage = new SecureSessionStorage();
            _callbackListener = new LoopbackCallbackListener();
            _installationService = new InstallationService(_clientFactory);
            _authService = new AuthService(_clientFactory, _secureStorage, _callbackListener, _installationService);
        }

        public IAuthService AuthService => _authService;

        public IAuthStatusProvider AuthStatusProvider => _authService;
    }
}
