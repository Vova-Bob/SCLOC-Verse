using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SCLOCVerse.Helpers
{
    /// <summary>
    /// Stateless-перевірка: чи активне (foreground) вікно належить процесу Star Citizen.
    /// Спільний Foreground Gate для модулів, що не повинні втручати у ввод поза грою
    /// (Anti-AFK, Auto Key).
    ///
    /// Ланцюжок: GetForegroundWindow → GetWindowThreadProcessId → OpenProcess(LIMITED)
    /// → QueryFullProcessImageName → Path.GetFileName → «StarCitizen.exe» (OrdinalIgnoreCase).
    /// Тихо повертає false, якщо вікна немає або OpenProcess не відкрився (без логів/винятків).
    /// </summary>
    public static class StarCitizenForeground
    {
        private const string StarCitizenProcessName = "StarCitizen.exe";
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const int MaxPath = 260;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(
            IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        /// <summary>
        /// True, якщо активне вікно належить процесу Star Citizen (StarCitizen.exe).
        /// </summary>
        public static bool IsStarCitizenForeground()
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return false;

            if (GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0)
                return false;

            var name = TryGetProcessName(pid);
            return string.Equals(name, StarCitizenProcessName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Повертає ім'я exe активного процесу (з розширенням), або null,
        /// якщо OpenProcess не відкрився або шлях не прочитано. Тихо — без логів/винятків.
        /// </summary>
        private static string? TryGetProcessName(uint pid)
        {
            var handle = OpenProcess(ProcessQueryLimitedInformation, bInheritHandle: false, pid);
            if (handle == IntPtr.Zero)
                return null;

            try
            {
                var buffer = new StringBuilder(MaxPath);
                int size = buffer.Capacity;
                if (QueryFullProcessImageName(handle, dwFlags: 0, buffer, ref size))
                    return Path.GetFileName(buffer.ToString());

                return null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }
    }
}
