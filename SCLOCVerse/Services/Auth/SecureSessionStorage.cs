using SCLOCVerse.Interfaces;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SCLOCVerse.Services.Auth
{
    /// <summary>
    /// Безпечне сховище сесії через DPAPI для поточного користувача.
    /// Реалізує вбудований інтерфейс persistence Supabase Gotrue.
    /// </summary>
    public sealed class SecureSessionStorage : ISecureSessionStorage, IGotrueSessionPersistence<Session>
    {
        private readonly string _filePath;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public SecureSessionStorage()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var directory = Path.Combine(localAppData, "SCLOCVerse");

            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            _filePath = Path.Combine(directory, ".auth");
        }

        public void SaveSession(Session session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            AuthForensics.Log("Storage.SaveSession", $"enter; path={_filePath}");

            try
            {
                var json = JsonSerializer.Serialize(session, JsonOptions);
                AuthForensics.Log("Storage.SaveSession", $"serialize OK; jsonLen={json.Length}");

                var bytes = Encoding.UTF8.GetBytes(json);
                AuthForensics.Log("Storage.SaveSession", $"getbytes OK; byteLen={bytes.Length}");

                var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                AuthForensics.Log("Storage.SaveSession", $"protect OK; protectedLen={protectedBytes.Length}");

                File.WriteAllBytes(_filePath, protectedBytes);
                AuthForensics.Log("Storage.SaveSession", $"write OK; fileExists={File.Exists(_filePath)}");
            }
            catch (Exception ex)
            {
                AuthForensics.Log("Storage.SaveSession", $"THREW at: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }

        public Session? LoadSession()
        {
            if (!File.Exists(_filePath))
                return null;

            try
            {
                var protectedBytes = File.ReadAllBytes(_filePath);
                var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(bytes);
                return JsonSerializer.Deserialize<Session>(json, JsonOptions);
            }
            catch (Exception)
            {
                DestroySession();
                return null;
            }
        }

        public void DestroySession()
        {
            DeleteRefreshToken();
        }

        public void SaveRefreshToken(string refreshToken)
        {
            // Legacy/compat: зберігаємо тільки refresh token у вигляді JSON-об'єкта,
            // щоб старі методи могли бути викликані без повної сесії.
            if (refreshToken == null)
                throw new ArgumentNullException(nameof(refreshToken));

            var session = new Session { RefreshToken = refreshToken };
            SaveSession(session);
        }

        public string? LoadRefreshToken()
        {
            var session = LoadSession();
            return session?.RefreshToken;
        }

        public void DeleteRefreshToken()
        {
            try
            {
                if (File.Exists(_filePath))
                    File.Delete(_filePath);
            }
            catch (Exception)
            {
                // Ігноруємо помилки видалення.
            }
        }
    }
}
