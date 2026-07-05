using SCLOCVerse.Interfaces;
using System;
using System.IO;
using System.Text;

namespace SCLOCVerse.Services.Autostart
{
    /// <summary>
    /// Реалізація автозапуску через HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
    ///
    /// Стиль роботи з реєстром узгоджений з існуючим InstallationService
    /// (Services/Auth/InstallationService.cs:223-249): OpenSubKey/CreateSubKey +
    /// try/catch з fallback-логуванням.
    ///
    /// Шлях до .exe визначається через Environment.ProcessPath — це надійно для
    /// Single File Publish (Assembly.Location у такому режимі порожній).
    /// </summary>
    public class AutostartService : IAutostartService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "SCLOCVerse";
        private const string MinimizedArg = "--minimized";

        /// <summary>
        /// Аргумент, з яким застосунок стартує при автозапуску з Windows.
        /// Вмикає старт прихованим у трей (App.xaml.cs:32).
        /// </summary>
        private static string BuildRunValue(string processPath)
        {
            // Лапки обов'язкові — шлях може містити пробіли (наприклад C:\Program Files\...).
            // Без лапок Windows спробує виконати лише частину до першого пробілу.
            return "\"" + processPath + "\" " + MinimizedArg;
        }

        public bool IsEnabled()
        {
            string? rawValue;
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                rawValue = key?.GetValue(RunValueName) as string;
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(rawValue))
                return false;

            // Витягуємо шлях до .exe з rawValue, ігноруючи аргументи командного рядка.
            var registeredPath = TryExtractExePath(rawValue);
            if (registeredPath == null)
                return false;

            var currentPath = GetCurrentProcessPath();
            if (currentPath == null)
                return false;

            // Порівняння нормалізованих шляхів (нечутливе до регистру на Windows).
            return string.Equals(
                NormalizePath(registeredPath),
                NormalizePath(currentPath),
                StringComparison.OrdinalIgnoreCase);
        }

        public void Enable()
        {
            var processPath = GetCurrentProcessPath()
                ?? throw new InvalidOperationException(
                    "Не вдалося визначити шлях до виконуваного файлу через Environment.ProcessPath. " +
                    "AutostartService не може увімкнути автозапуск у режимі Single File Publish без ProcessPath.");

            var value = BuildRunValue(processPath);

            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath);
                key?.SetValue(RunValueName, value, Microsoft.Win32.RegistryValueKind.String);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AutostartService] Не вдалося записати Run-ключ: {ex.Message}");
                throw;
            }
        }

        public void Disable()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                // throwOnMissingValue: false → ідемпотентно (якщо запису вже немає, не кидаємо).
                key?.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AutostartService] Не вдалося видалити Run-ключ: {ex.Message}");
            }
        }

        /// <summary>
        /// Повертає шлях до поточного процесу. Для Single File Publish це надійніше
        /// за Assembly.Location (який у single-file режимі порожній).
        /// </summary>
        private static string? GetCurrentProcessPath()
        {
            return Environment.ProcessPath;
        }

        /// <summary>
        /// Парсить значення Run-ключа й витягує шлях до .exe, відкидаючи аргументи.
        /// Обробляє два формати Windows Run-ключа:
        ///   1. "C:\Path\app.exe" --arg   (рекомендований, з лапками)
        ///   2. C:\Path\app.exe --arg     (без лапок, якщо шлях без пробілів)
        /// </summary>
        private static string? TryExtractExePath(string rawValue)
        {
            var trimmed = rawValue.Trim();
            if (trimmed.Length == 0)
                return null;

            // Варіант 1: шлях у лапках.
            if (trimmed[0] == '"')
            {
                var closingQuote = trimmed.IndexOf('"', 1);
                if (closingQuote <= 0)
                    return null;
                return trimmed.Substring(1, closingQuote - 1);
            }

            // Варіант 2: шлях без лапок — перший токен до першого пробілу.
            var firstSpace = trimmed.IndexOf(' ');
            return firstSpace < 0 ? trimmed : trimmed.Substring(0, firstSpace);
        }

        /// <summary>
        /// Нормалізує шлях для стабільного порівняння (повний абсолютний шлях).
        /// </summary>
        private static string NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path;
            }
        }
    }
}
