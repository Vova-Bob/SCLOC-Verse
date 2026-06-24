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
        private readonly IDiscordGuildSyncService _guildSyncService;
        private readonly ISessionTrackerService _sessionTracker;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private bool _disposed;

        public AuthService(
            ISupabaseClientFactory clientFactory,
            ISecureSessionStorage secureStorage,
            ILoopbackCallbackListener callbackListener,
            IInstallationService installationService,
            IDiscordGuildSyncService guildSyncService,
            ISessionTrackerService sessionTracker)
        {
            _supabase = clientFactory?.CreateClient() ?? throw new ArgumentNullException(nameof(clientFactory));
            _secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
            _callbackListener = callbackListener ?? throw new ArgumentNullException(nameof(callbackListener));
            _installationService = installationService ?? throw new ArgumentNullException(nameof(installationService));
            _guildSyncService = guildSyncService ?? throw new ArgumentNullException(nameof(guildSyncService));
            _sessionTracker = sessionTracker ?? throw new ArgumentNullException(nameof(sessionTracker));

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
                        Scopes = "identify guilds"
                    }).ConfigureAwait(false);

                if (state?.Uri == null)
                    return new AuthResult.Failure("Не вдалося отримати URL для входу.");

                OpenBrowser(state.Uri.ToString());

                var callbackUrl = await _callbackListener.WaitForCallbackAsync(cancellationToken).ConfigureAwait(false);

                await _callbackListener.StopAsync().ConfigureAwait(false);

                if (callbackUrl == null)
                    return new AuthResult.Cancelled();

                var code = GetQueryParameter(callbackUrl, "code");
                var errorCode = GetQueryParameter(callbackUrl, "error");
                var errorDescription = GetQueryParameter(callbackUrl, "error_description");

                // Відмова користувача від авторизації — не є помилкою, повертаємо Cancelled,
                // щоб UI не показував діалогове вікно з помилкою.
                if (string.Equals(errorCode, "access_denied", StringComparison.OrdinalIgnoreCase))
                    return new AuthResult.Cancelled();

                if (!string.IsNullOrWhiteSpace(errorCode) || !string.IsNullOrWhiteSpace(errorDescription))
                    return new AuthResult.Failure(errorDescription ?? errorCode ?? "Помилка авторизації");

                if (string.IsNullOrWhiteSpace(code))
                    return new AuthResult.Failure("Авторизаційний код відсутній.");

                var pkceVerifier = state.PKCEVerifier ?? throw new InvalidOperationException("PKCE verifier is missing.");
                var session = await _supabase.Auth.ExchangeCodeForSession(pkceVerifier, code).ConfigureAwait(false);

                if (session == null)
                    return new AuthResult.Failure("Не вдалося обміняти код на сесію.");

                SaveSession(session);
                await SyncProfileAsync(session).ConfigureAwait(false);
                await _installationService.SyncCurrentInstallationAsync(cancellationToken).ConfigureAwait(false);
                await SyncGuildsAsync(session, cancellationToken).ConfigureAwait(false);
                await _sessionTracker.StartSessionAsync(cancellationToken).ConfigureAwait(false);

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
            var savedSession = _secureStorage.LoadSession();

            // Потребуємо access token: без нього клієнт не зможе виконати жодного запиту,
            // а restore через єдиний refresh token у цій версії Gotrue ненадійний.
            if (savedSession == null || string.IsNullOrWhiteSpace(savedSession.AccessToken))
                return false;

            try
            {
                await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    // Гідратуємо клієнт збереженими токенами напряму.
                    // RetrieveSessionAsync() у цій версії Gotrue повертає null при restore
                    // і раніше призводив до деструктивного видалення .auth.
                    await _supabase.Auth.SetSession(
                        savedSession.AccessToken!,
                        savedSession.RefreshToken ?? string.Empty,
                        true).ConfigureAwait(false);

                    // Профіль відновлюємо з повної збереженої сесії (містить User),
                    // щоб UI показував аватар навіть без мережевого запиту.
                    await SyncProfileAsync(savedSession).ConfigureAwait(false);

                    // Синхронізацію інсталяції не вважаємо критичною для restore:
                    // її збій не повинен скидати відновлений профіль.
                    try
                    {
                        await _installationService.SyncCurrentInstallationAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception syncEx)
                    {
                        LogError("Installation sync during restore failed", syncEx);
                    }

                    // Синхронізація Discord-гільдій теж best-effort: не ламає restore.
                    await SyncGuildsAsync(savedSession, cancellationToken).ConfigureAwait(false);
                    await _sessionTracker.StartSessionAsync(cancellationToken).ConfigureAwait(false);

                    return true;
                }
                finally
                {
                    _refreshLock.Release();
                }
            }
            catch (Exception ex)
            {
                // Не знищуємо .auth при мережевих/транзитних помилках:
                // сесія може бути валідною, а збій — тимчасовим.
                LogError("TryRestoreSession failed", ex);
                SetState(AuthState.SignedOut);
                return false;
            }
        }

        public async Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await _sessionTracker.EndSessionAsync(cancellationToken).ConfigureAwait(false);
                await _supabase.Auth.SignOut(Supabase.Gotrue.Constants.SignOutScope.Global).ConfigureAwait(false);
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
            // Зберігаємо повну сесію, щоб при наступному запуску можна було
            // відновити access token без обов'язкового мережевого запиту.
            if (!string.IsNullOrWhiteSpace(session.RefreshToken))
                _secureStorage.SaveSession(session);
        }

        // Синхронізація Discord-гільдій не повинна ламати логін/restore: ізольований best-effort.
        private async Task SyncGuildsAsync(Session session, CancellationToken cancellationToken)
        {
            try
            {
                await _guildSyncService.SyncUserGuildsAsync(session, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogError("Discord guild sync failed", ex);
            }
        }

        private async Task SyncProfileAsync(Session session)
        {
            // Профіль беремо з авторитетного GetUser(accessToken): свіжий об'єкт із сервера
            // не пошкоджується серіалізацією сесії (вкладений custom_claims.global_name
            // інакше перетворюється на порожній масив при restore).
            User? user = null;
            var accessToken = _supabase.Auth.CurrentSession?.AccessToken ?? session.AccessToken;
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                try
                {
                    user = await _supabase.Auth.GetUser(accessToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogError("GetUser for profile failed", ex);
                }
            }

            // Fallback на дані сесії, якщо мережевий запит не вдався.
            user ??= session.User;

            Profile = user != null ? DiscordUserProfileMapper.Map(user) : null;

            SetState(Profile != null ? AuthState.SignedIn : AuthState.Error);
        }

        private void OnAuthStateChanged(object sender, GotrueConstants.AuthState stateChanged)
        {
            // Обробляємо тільки вихід з системи.
            if (stateChanged == GotrueConstants.AuthState.SignedOut)
            {
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
