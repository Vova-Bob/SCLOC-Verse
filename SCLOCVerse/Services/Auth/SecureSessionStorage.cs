using SCLOCVerse.Interfaces;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SCLOCVerse.Services.Auth
{
    /// <summary>
    /// Безпечне сховище refresh token через DPAPI для поточного користувача.
    /// </summary>
    public sealed class SecureSessionStorage : ISecureSessionStorage
    {
        private readonly string _filePath;

        public SecureSessionStorage()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var directory = Path.Combine(localAppData, "SCLOCVerse");

            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            _filePath = Path.Combine(directory, ".auth");
        }

        public void SaveRefreshToken(string refreshToken)
        {
            if (refreshToken == null)
                throw new ArgumentNullException(nameof(refreshToken));

            var bytes = Encoding.UTF8.GetBytes(refreshToken);
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_filePath, protectedBytes);
        }

        public string? LoadRefreshToken()
        {
            if (!File.Exists(_filePath))
                return null;

            try
            {
                var protectedBytes = File.ReadAllBytes(_filePath);
                var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception)
            {
                DeleteRefreshToken();
                return null;
            }
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
