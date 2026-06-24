using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Auth;
using Supabase;
using Supabase.Gotrue;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using GotrueConstants = Supabase.Gotrue.Constants;

namespace SCLOCVerse.Services.Auth
{
    /// <summary>
    /// Головний сервіс аутентифікації Discord OAuth через Supabase.
    /// </summary>
    public sealed class AuthService : IAuthService, IDisposable
    {
        private readonly Supabase.Client _supabase;
        private readonly ISecureSessionStorage _secureStorage;
        private readonly ILoopbackCallbackListener _callbackListener;
        private readonly IInstallationService _installationService;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private bool _disposed;

        public AuthService(
            ISupabaseClientFactory clientFactory,
            ISecureSessionStorage secureStorage,
            ILoopbackCallbackListener callbackListener,
            IInstallationService installationService)
        {
            _supabase = clientFactory?.CreateClient() ?? throw new ArgumentNullException(nameof(clientFactory));
            _secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
            _callbackListener = callbackListener ?? throw new ArgumentNullException(nameof(callbackListener));
            _installationService = installationService ?? throw new ArgumentNullException(nameof(installationService));

            _supabase.Auth.AddStateChangedListener(OnAuthStateChanged);
        }

        public AuthState State { get; private set; }

        public DiscordUserProfile? Profile { get; private set; }

        public event EventHandler? StatusChanged;

        public async Task<AuthResult> SignInAsync(CancellationToken cancellationToken = default)
        {
            if (State == AuthState.SigningIn)
                return new AuthResult.Failure("Вхід вже виконується.");

            SetState(AuthState.SigningIn);

            try
            {
                var redirectUri = await _callbackListener.StartAsync(cancellationToken).ConfigureAwait(false);

                var state = await _supabase.Auth.SignIn(
                    GotrueConstants.Provider.Discord,
                    new SignInOptions
                    {
                        FlowType = GotrueConstants.OAuthFlowType.PKCE,
                        RedirectTo = redirectUri.ToString(),
                        Scopes = "identify"
                    }).ConfigureAwait(false);

                if (state?.Uri == null)
                    return new AuthResult.Failure("Не вдалося отримати URL для входу.");

                OpenBrowser(state.Uri.ToString());

                var callbackUrl = await _callbackListener.WaitForCallbackAsync(cancellationToken).ConfigureAwait(false);

                await _callbackListener.StopAsync().ConfigureAwait(false);

                if (callbackUrl == null)
                    return new AuthResult.Cancelled();

                var code = GetQueryParameter(callbackUrl, "code");
                var error = GetQueryParameter(callbackUrl, "error_description") ?? GetQueryParameter(callbackUrl, "error");

                if (!string.IsNullOrWhiteSpace(error))
                    return new AuthResult.Failure(error);

                if (string.IsNullOrWhiteSpace(code))
                    return new AuthResult.Failure("Авторизаційний код відсутній.");

                var pkceVerifier = state.PKCEVerifier ?? throw new InvalidOperationException("PKCE verifier is missing.");
                var session = await _supabase.Auth.ExchangeCodeForSession(pkceVerifier, code).ConfigureAwait(false);

                if (session == null)
                {
                    AuthForensics.Log("SignIn", "ExchangeCodeForSession returned NULL");
                    return new AuthResult.Failure("Не вдалося обміняти код на сесію.");
                }

                // FORENSICS: фіксуємо значення об'єкта Session після ExchangeCodeForSession.
                AuthForensics.Log("SignIn",
                    $"session OK; AccessTokenLen={session.AccessToken?.Length ?? -1}; " +
                    $"RefreshTokenLen={session.RefreshToken?.Length ?? -1}; " +
                    $"ExpiresIn={session.ExpiresIn}; TokenType={session.TokenType}; " +
                    $"UserNull={session.User == null}; ProviderTokenNull={session.ProviderToken == null}");

                SaveSession(session);
                await SyncProfileAsync(session).ConfigureAwait(false);
                await _installationService.SyncCurrentInstallationAsync(cancellationToken).ConfigureAwait(false);

                return new AuthResult.Success(Profile!);
            }
            catch (OperationCanceledException)
            {
                return new AuthResult.Cancelled();
            }
            catch (Exception ex)
            {
                LogError("SignIn failed", ex);
                return new AuthResult.Failure($"Помилка входу: {ex.Message}");
            }
            finally
            {
                if (State == AuthState.SigningIn)
                    SetState(AuthState.SignedOut);

                await _callbackListener.StopAsync().ConfigureAwait(false);
            }
        }

        public async Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default)
        {
            AuthForensics.Log("TryRestoreSession", "enter");
            var savedSession = _secureStorage.LoadSession();
            AuthForensics.Log("TryRestoreSession",
                $"LoadSession -> {(savedSession == null ? "NULL" : $"session; refreshLen={savedSession.RefreshToken?.Length ?? -1}")}");
            if (savedSession == null || string.IsNullOrWhiteSpace(savedSession.RefreshToken))
                return false;

            try
            {
                await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    // Завантажуємо збережену сесію з persistence та оновлюємо access token.
                    var session = await _supabase.Auth.RetrieveSessionAsync().ConfigureAwait(false);

                    if (session == null)
                    {
                        _secureStorage.DeleteRefreshToken();
                        SetState(AuthState.SignedOut);
                        return false;
                    }

                    SaveSession(session);
                    await SyncProfileAsync(session).ConfigureAwait(false);
                    await _installationService.SyncCurrentInstallationAsync(cancellationToken).ConfigureAwait(false);

                    return true;
                }
                finally
                {
                    _refreshLock.Release();
                }
            }
            catch (Exception ex)
            {
                LogError("TryRestoreSession failed", ex);
                _secureStorage.DeleteRefreshToken();
                SetState(AuthState.SignedOut);
                return false;
            }
        }

        public async Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await _supabase.Auth.SignOut().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogError("SignOut server call failed", ex);
            }
            finally
            {
                _secureStorage.DeleteRefreshToken();
                Profile = null;
                SetState(AuthState.SignedOut);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _supabase.Auth.RemoveStateChangedListener(OnAuthStateChanged);
            _callbackListener.Dispose();
            _refreshLock.Dispose();
        }

        private void SaveSession(Session session)
        {
            var hasRefresh = !string.IsNullOrWhiteSpace(session.RefreshToken);
            AuthForensics.Log("AuthService.SaveSession",
                $"enter; hasRefreshToken={hasRefresh}; refreshLen={session.RefreshToken?.Length ?? -1}");

            // Зберігаємо повну сесію, щоб при наступному запуску можна було
            // відновити access token без обов'язкового мережевого запиту.
            if (hasRefresh)
            {
                AuthForensics.Log("AuthService.SaveSession", "calling _secureStorage.SaveSession");
                try
                {
                    _secureStorage.SaveSession(session);
                    AuthForensics.Log("AuthService.SaveSession", "_secureStorage.SaveSession returned OK");
                }
                catch (Exception ex)
                {
                    AuthForensics.Log("AuthService.SaveSession", $"THREW: {ex.GetType().Name}: {ex.Message}");
                    throw;
                }
            }
            else
            {
                AuthForensics.Log("AuthService.SaveSession", "SKIPPED (no refresh token)");
            }
        }

        private async Task SyncProfileAsync(Session session)
        {
            if (session.User != null)
            {
                Profile = DiscordUserProfileMapper.Map(session.User);
            }
            else
            {
                var accessToken = session.AccessToken ?? string.Empty;
                var user = await _supabase.Auth.GetUser(accessToken).ConfigureAwait(false);
                Profile = user != null ? DiscordUserProfileMapper.Map(user) : null;
            }

            SetState(Profile != null ? AuthState.SignedIn : AuthState.Error);
        }

        private void OnAuthStateChanged(object sender, GotrueConstants.AuthState stateChanged)
        {
            AuthForensics.Log("OnAuthStateChanged", $"state={stateChanged}");

            // Обробляємо тільки вихід з системи.
            if (stateChanged == GotrueConstants.AuthState.SignedOut)
            {
                AuthForensics.Log("OnAuthStateChanged", "SignedOut received -> deleting .auth");
                Profile = null;
                _secureStorage.DeleteRefreshToken();
                SetState(AuthState.SignedOut);
            }
        }

        private void SetState(AuthState state)
        {
            State = state;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        private static void OpenBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LogError("OpenBrowser failed", ex);
            }
        }

        private static string? GetQueryParameter(Uri url, string key)
        {
            var query = HttpUtility.ParseQueryString(url.Query);
            var value = query.Get(key);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static void LogError(string message, Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AuthService] {message}: {ex}");
        }
    }
}
