using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace SCLOCVerse.Helpers
{
    /// <summary>
    /// Тимчасовий helper для forensic діагностики shutdown-затримки.
    /// Записує в %TEMP%\SCLOCVerse\shutdown-timing.log із негайним flush.
    /// </summary>
    internal static class ShutdownTimingLogger
    {
        private static readonly string LogDirectory = Path.Combine(Path.GetTempPath(), "SCLOCVerse");
        private static readonly string LogFilePath = Path.Combine(LogDirectory, "shutdown-timing.log");
        private static readonly object Lock = new object();

        public static void Log(string message)
        {
            try
            {
                string line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
                lock (Lock)
                {
                    Directory.CreateDirectory(LogDirectory);
                    File.AppendAllText(LogFilePath, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Ігноруємо помилки логування під час shutdown.
            }
        }
    }
}
