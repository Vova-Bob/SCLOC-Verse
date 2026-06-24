using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Auth;
using AppSession = SCLOCVerse.Models.Auth.AppSession;
using Supabase;
using Supabase.Postgrest;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Services.Auth
{
    /// <summary>
    /// Відстежує життєвий цикл сесії додатка в таблиці public.sessions.
    /// Створює запис після авторизації, закриває при виході або завершенні додатка.
    /// </summary>
    public sealed class SessionTrackerService : ISessionTrackerService
    {
        private readonly Supabase.Client _supabase;
        private readonly string _installId;
        private Guid? _currentSessionId;

        public SessionTrackerService(ISupabaseClientFactory clientFactory, IInstallationService installationService)
        {
            _supabase = clientFactory?.CreateClient() ?? throw new ArgumentNullException(nameof(clientFactory));
            _installId = installationService?.InstallId ?? throw new ArgumentNullException(nameof(installationService));
        }

        public async Task StartSessionAsync(CancellationToken cancellationToken = default)
        {
            var user = _supabase.Auth.CurrentUser;
            if (user == null || !Guid.TryParse(user.Id?.ToString(), out var userId))
                return;

            // Закриваємо незавершені сесії цієї інсталяції перед створенням нової.
            await CloseOrphanSessionsAsync(cancellationToken).ConfigureAwait(false);

            var now = DateTimeOffset.UtcNow;
            var session = new AppSession
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                InstallId = _installId,
                StartedAt = now,
                AppVersion = GetCurrentAppVersion(),
                OsVersion = Environment.OSVersion.VersionString
            };

            await _supabase
                .From<AppSession>()
                .Insert(session, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _currentSessionId = session.Id;

            // Оновлюємо last_session_at та last_seen в app_installations.
            await UpdateInstallationActivityAsync(userId, now, cancellationToken).ConfigureAwait(false);
        }

        public async Task EndSessionAsync(CancellationToken cancellationToken = default)
        {
            if (_currentSessionId == null)
                return;

            var endedAt = DateTimeOffset.UtcNow;
#pragma warning disable CS8603
            await _supabase
                .From<AppSession>()
                .Where(s => s.Id == _currentSessionId.Value)
                .Set(s => s.EndedAt, endedAt)
                .Update(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
#pragma warning restore CS8603

            _currentSessionId = null;
        }

        private async Task CloseOrphanSessionsAsync(CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
#pragma warning disable CS8603
            await _supabase
                .From<AppSession>()
                .Filter("install_id", Supabase.Postgrest.Constants.Operator.Equals, _installId)
                .Filter("ended_at", Supabase.Postgrest.Constants.Operator.Is, "null")
                .Set(s => s.EndedAt, now)
                .Update(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
#pragma warning restore CS8603
        }

        private async Task UpdateInstallationActivityAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var installation = new AppInstallation
            {
                InstallId = _installId,
                UserId = userId,
                LastSessionAt = now,
                LastSeen = now,
                UpdatedAt = now,
                IsActive = true
            };

            await _supabase
                .From<AppInstallation>()
                .Upsert(installation, new QueryOptions { OnConflict = "install_id" }, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private static string? GetCurrentAppVersion()
        {
            try
            {
                var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
                return version?.ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}
