namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Керує автозапуском застосунку з Windows через HKCU Run-ключ.
    ///
    /// Принцип відповідальності: цей сервіс — єдине джерело істини для Run-ключа.
    /// Інсталятор (.iss) НЕ пише й НЕ видаляє Run-ключ — лише встановлює файли.
    /// Хто створив ресурс — той його й видаляє.
    ///
    /// Ланцюг: Settings (UI, Етап D) → AutostartService → Registry.
    /// </summary>
    public interface IAutostartService
    {
        /// <summary>
        /// true, якщо Run-ключ існує И вказує на поточний exe-шлях.
        /// Аргументи командного рядка (--minimized, --silent тощо) ігноруються —
        /// порівнюється лише шлях до виконуваного файлу.
        /// </summary>
        bool IsEnabled();

        /// <summary>
        /// Створює запис у HKCU\Software\Microsoft\Windows\CurrentVersion\Run
        /// зі значенням "&lt;processPath&gt;" --minimized.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Environment.ProcessPath повернув null — критична ситуація для Single File Publish.
        /// </exception>
        void Enable();

        /// <summary>
        /// Видаляє запис з HKCU\...\Run. Ідемпотентний — не кидає, якщо запис відсутній.
        /// </summary>
        void Disable();
    }
}
