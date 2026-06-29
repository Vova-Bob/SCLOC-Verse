using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SCLOCVerse.Services.InputSystem.Diagnostics
{
    /// <summary>
    /// Тимчасова діагностична система для встановлення причини несправності гарячих клавіш.
    /// Записує всі події в файл у AppData/Local/SCLOCVerse/Logs/input-diagnostics.log.
    /// </summary>
    public static class InputDiagnostics
    {
        private static readonly object Sync = new();
        private static readonly string LogFilePath;

        static InputDiagnostics()
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SCLOCVerse",
                "Logs");

            Directory.CreateDirectory(folder);
            LogFilePath = Path.Combine(folder, "input-diagnostics.log");
        }

        /// <summary>
        /// Записує діагностичне повідомлення.
        /// </summary>
        public static void Write(string source, string message)
        {
            lock (Sync)
            {
                var activeWindowTitle = GetActiveWindowTitle();
                var activeWindowHandle = GetForegroundWindow();
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{source}] HWND=0x{activeWindowHandle:X} Title=\"{activeWindowTitle}\" | {message}";

                System.Diagnostics.Debug.WriteLine(line);

                try
                {
                    File.AppendAllText(LogFilePath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[InputDiagnostics] Failed to write log: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Повертає останній код помилки Win32.
        /// </summary>
        public static int GetWin32Error()
        {
            return Marshal.GetLastWin32Error();
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        private static string GetActiveWindowTitle()
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return "(none)";

            var builder = new StringBuilder(512);
            int length = GetWindowText(hwnd, builder, builder.Capacity);
            return length > 0 ? builder.ToString() : "(untitled)";
        }
    }
}
