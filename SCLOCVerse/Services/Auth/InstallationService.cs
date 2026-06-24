using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Auth;
using Supabase;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Services.Auth
{
    /// <summary>
    /// Синхронізує метадані поточної інсталяції в таблиці public.installations.
    /// </summary>
    public sealed class InstallationService : IInstallationService
    {
        private readonly Client _supabase;
        private readonly string _installId;

        public InstallationService(ISupabaseClientFactory clientFactory)
        {
            _supabase = clientFactory?.CreateClient() ?? throw new ArgumentNullException(nameof(clientFactory));
            _installId = GetOrCreateInstallId();
        }

        public async Task SyncCurrentInstallationAsync(CancellationToken cancellationToken = default)
        {
            var user = _supabase.Auth.CurrentUser;
            if (user == null)
                return;

            var userIdString = user.Id?.ToString();
            if (!Guid.TryParse(userIdString, out var userId))
                return;

            var response = await _supabase
                .From<InstallationInfo>()
                .Where(x => x.UserId == userId && x.InstallId == _installId)
                .Get(cancellationToken)
                .ConfigureAwait(false);

            var now = DateTimeOffset.UtcNow;

            if (response.Models.Count == 0)
            {
                var installation = new InstallationInfo
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    InstallId = _installId,
                    DeviceName = Environment.MachineName,
                    LastSeenAt = now,
                    CreatedAt = now
                };

                await _supabase
                    .From<InstallationInfo>()
                    .Insert(installation, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                var installation = response.Models[0];
                installation.LastSeenAt = now;
                installation.DeviceName = Environment.MachineName;

                await _supabase
                    .From<InstallationInfo>()
                    .Update(installation, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private static string GetOrCreateInstallId()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var directory = System.IO.Path.Combine(localAppData, "SCLOCVerse");

            if (!System.IO.Directory.Exists(directory))
                System.IO.Directory.CreateDirectory(directory);

            var filePath = System.IO.Path.Combine(directory, "install-id");

            if (System.IO.File.Exists(filePath))
            {
                var id = System.IO.File.ReadAllText(filePath);
                if (!string.IsNullOrWhiteSpace(id))
                    return id;
            }

            var newId = Guid.NewGuid().ToString("N");
            System.IO.File.WriteAllText(filePath, newId);
            return newId;
        }
    }
}
