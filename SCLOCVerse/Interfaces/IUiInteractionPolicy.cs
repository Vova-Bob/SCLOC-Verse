namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Політика взаємодії з UI, що визначає, які елементи інтерфейсу дозволені
    /// у поточному режимі запуску. Замість передачі bool-прапорців з App.xaml.cs
    /// у MainWindow — політика інкапсулює контекст запуску.
    ///
    /// Майбутні сценарії (URL Handler, Planuvannik, post-update, headless) можуть
    /// задати свої значення без розростання конструктора MainWindow.
    /// </summary>
    public interface IUiInteractionPolicy
    {
        /// <summary>
        /// Чи дозволено показувати модальні діалоги (наприклад, очищення кешу шейдерів).
        /// false при --minimized старті — діалог підняв би приховане вікно.
        /// </summary>
        bool CanShowModalDialogs { get; }

        /// <summary>
        /// Чи дозволено запускати стартові промпти (in-app toast про папку гри).
        /// false при --minimized старті.
        /// </summary>
        bool CanShowStartupPrompts { get; }

        /// <summary>
        /// Чи дозволено показувати in-app toasts (анімовані поверх вікна).
        /// Зазвичай true — приховане в треї вікно їх не показує, тож не заважає.
        /// </summary>
        bool CanShowInAppToast { get; }
    }
}
