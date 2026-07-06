using SCLOCVerse.Interfaces;

namespace SCLOCVerse.Models.ApplicationUpdate
{
    /// <summary>
    /// Результат одного циклу BackgroundUpdateOrchestrator (послідовно App → Localization → LIA).
    /// null у відповідному полі означає, що перевірка цього джерела не виконана
    /// (Settings вимкнено, game folder не знайдено, помилка — делібератно пропущено).
    ///
    /// NotificationRouter споживає цей DTO та будує NotificationCandidate[] для MainWindow.
    /// </summary>
    public sealed record UpdateCycleResult(
        UpdateCheckResult? AppUpdate,
        IReadOnlyList<LocalizationInstallResult> Localization,
        LiaInstallStatus? LiaStatus);
}