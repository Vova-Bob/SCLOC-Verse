using System;
using System.IO;
using System.Text;

namespace SCLOCVerse.Services.Auth
{
    /// <summary>
    /// ТИМЧАСОВА діагностика ланцюжка збереження сесії (Runtime Forensics).
    /// Підлягає видаленню після встановлення root cause.
    /// </summary>
    internal static class AuthForensics
    {
        private static readonly object _lock = new();

        internal static void Log(string scope, string message)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SCLOCVerse");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "auth-forensics.log");

                var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{scope}] {message}{Environment.NewLine}";

                lock (_lock)
                {
                    File.AppendAllText(path, line, Encoding.UTF8);
                }
            }
            catch
            {
                // Діагностика ніколи не повинна ламати основний потік.
            }
        }
    }
}
