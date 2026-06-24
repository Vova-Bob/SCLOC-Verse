using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Auth;
using Supabase;
using Supabase.Postgrest;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Services.Auth
{
    /// <summary>
    /// Синхронізує метадані поточної інсталяції в таблиці public.app_installations.
    /// </summary>
    public sealed class InstallationService : IInstallationService
    {
        private readonly Supabase.Client _supabase;
        private readonly string _installId;

        public string InstallId => _installId;

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



            var now = DateTimeOffset.UtcNow;
            var appVersion = GetCurrentAppVersion();

            var installation = new AppInstallation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                InstallId = _installId,
                AppVersion = appVersion,
                Platform = "Windows",
                MachineId = Environment.MachineName,
                OsVersion = Environment.OSVersion.VersionString,
                LastSeen = now,
                UpdatedAt = now,
                IsActive = true
            };

            // Upsert за бізнес-ключем install_id. first_seen та created_at
            // не передаються з C# — вони заповнюються DEFAULT now() при INSERT
            // і не змінюються при UPDATE.
            await _supabase
                .From<AppInstallation>()
                .Upsert(
                    installation,
                    new QueryOptions { OnConflict = "install_id" },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private static string GetOrCreateInstallId()
        {
            const string registrySubKey = @"Software\VALDEUS\SCLOCVerse";
            const string registryValueName = "InstallId";

            var fileId = ReadInstallIdFromFile();
            var registryId = ReadInstallIdFromRegistry(registrySubKey, registryValueName);

            // Case 1: файл і реєстр існують і співпадають.
            if (!string.IsNullOrWhiteSpace(fileId) &&
                !string.IsNullOrWhiteSpace(registryId) &&
                string.Equals(fileId, registryId, StringComparison.OrdinalIgnoreCase))
            {
                return fileId;
            }

            // Case 2: файл є, реєстру немає — відновлюємо реєстр.
            if (!string.IsNullOrWhiteSpace(fileId) && string.IsNullOrWhiteSpace(registryId))
            {
                WriteInstallIdToRegistry(registrySubKey, registryValueName, fileId);
                return fileId;
            }

            // Case 3: файлу немає, реєстр є — відновлюємо файл.
            if (string.IsNullOrWhiteSpace(fileId) && !string.IsNullOrWhiteSpace(registryId))
            {
                WriteInstallIdToFile(registryId);
                return registryId;
            }

            // Case 5: обидва є, але різні — файл є джерелом істини.
            if (!string.IsNullOrWhiteSpace(fileId) && !string.IsNullOrWhiteSpace(registryId))
            {
                System.Diagnostics.Debug.WriteLine($"[InstallId] Конфлікт: файл={fileId}, реєстр={registryId}. Перемагає файл.");
                WriteInstallIdToRegistry(registrySubKey, registryValueName, fileId);
                return fileId;
            }

            // Case 4: обидва відсутні — генеруємо новий і зберігаємо в обидва джерела.
            var newId = Guid.NewGuid().ToString("N");
            WriteInstallIdToFile(newId);
            WriteInstallIdToRegistry(registrySubKey, registryValueName, newId);
            return newId;
        }

        private static string? ReadInstallIdFromFile()
        {
            var filePath = GetInstallIdFilePath();
            if (!System.IO.File.Exists(filePath))
                return null;

            var id = System.IO.File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            return string.IsNullOrWhiteSpace(id) ? null : id.Trim();
        }

        private static void WriteInstallIdToFile(string installId)
        {
            var directory = GetInstallIdDirectory();
            System.IO.Directory.CreateDirectory(directory);
            var filePath = System.IO.Path.Combine(directory, "install-id");
            System.IO.File.WriteAllText(filePath, installId, System.Text.Encoding.UTF8);
        }

        private static string? ReadInstallIdFromRegistry(string subKey, string valueName)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subKey, false);
                var value = key?.GetValue(valueName) as string;
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }
            catch
            {
                return null;
            }
        }

        private static void WriteInstallIdToRegistry(string subKey, string valueName, string installId)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(subKey);
                key?.SetValue(valueName, installId, Microsoft.Win32.RegistryValueKind.String);
            }
            catch
            {
                // Fallback: реєстр недоступний, продовжуємо працювати з файлом.
                System.Diagnostics.Debug.WriteLine("[InstallId] Не вдалося записати InstallId у реєстр.");
            }
        }

        private static string GetInstallIdDirectory()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return System.IO.Path.Combine(localAppData, "SCLOCVerse");
        }

        private static string GetInstallIdFilePath()
        {
            return System.IO.Path.Combine(GetInstallIdDirectory(), "install-id");
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
