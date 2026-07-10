using SCLOCVerse.Models.AntiAfk;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Налаштування-переваги користувача (поведінка закриття, автооновлення,
    /// dedup OS-тостів). Реалізується через існуючий SettingsService (Варіант A)
    /// — один об'єкт реалізує ISettingsService + IUpdateChannelService + IPreferencesService.
    ///
    /// Зберігання: %LocalAppData%\SCLOCVerse\user.config через ApplicationSettingsBase.
    /// Авто-міграція версій: MigrateSettingsIfNeeded в App.xaml.cs.
    /// </summary>
    public interface IPreferencesService
    {
        // --- Поведінка закриття вікна ---

        /// <summary>
        /// true, якщо кнопка X / Alt+F4 / Taskbar Close згортають в трей.
        /// false — повний вихід через Shutdown.
        /// Default: True (зберігає поточну поведінку релізу).
        /// </summary>
        bool GetMinimizeToTray();
        void SetMinimizeToTray(bool value);

        // --- Автооновлення локалізації (Етап E) ---

        /// <summary>
        /// true, якщо оркестратор має автоматично перевіряти й застосовувати
        /// оновлення global.ini. Default: False (користувач має сам увімкнути).
        /// </summary>
        bool GetAutoUpdateLocalization();
        void SetAutoUpdateLocalization(bool value);

        /// <summary>
        /// true, якщо увімкнено розширену діагностику (детальне логування,
        /// трейс операцій оновлення/IPC/tray). Default: False.
        /// </summary>
        bool GetAdvancedDiagnostics();
        void SetAdvancedDiagnostics(bool value);

        // --- Toast Dedup (Етап E/F) ---

        /// <summary>Остання версія локалізації, про яку вже був OS-тост.</summary>
        string GetLastLocalizationToast();
        void SetLastLocalizationToast(string version);

        /// <summary>Остання версія L.I.A, про яку вже був OS-тост.</summary>
        string GetLastLiaToast();
        void SetLastLiaToast(string version);

        /// <summary>Остання версія застосунку, про яку вже був OS-тост.</summary>
        string GetLastAppToast();
        void SetLastAppToast(string version);

        // --- Діагностика ---

        /// <summary>
        /// UTC-час останнього показаного OS-тоста (ISO 8601). Тільки для діагностики.
        /// </summary>
        string GetLastToastTimestampUtc();
        void SetLastToastTimestampUtc(string timestamp);

        // --- Anti-AFK ---

        /// <summary>Чи увімкнено Anti-AFK. Default: False.</summary>
        bool GetAntiAfkEnabled();
        void SetAntiAfkEnabled(bool value);

        /// <summary>Колір індикатора як hex-рядок ("#4CAF50"), або "Hidden".</summary>
        string GetAntiAfkIndicatorColor();
        void SetAntiAfkIndicatorColor(string value);

        /// <summary>Позиція індикатора на екрані. Default: TopRight.</summary>
        AntiAfkIndicatorPosition GetAntiAfkIndicatorPosition();
        void SetAntiAfkIndicatorPosition(AntiAfkIndicatorPosition value);

        /// <summary>Розмір індикатора в пікселях (8–32). Default: 12.</summary>
        double GetAntiAfkIndicatorSize();
        void SetAntiAfkIndicatorSize(double value);

        /// <summary>Тип анімації пульсації. Default: Pulse.</summary>
        AntiAfkIndicatorAnimation GetAntiAfkIndicatorAnimation();
        void SetAntiAfkIndicatorAnimation(AntiAfkIndicatorAnimation value);

        /// <summary>Режим видимості індикатора. Default: Running.</summary>
        AntiAfkIndicatorMode GetAntiAfkIndicatorMode();
        void SetAntiAfkIndicatorMode(AntiAfkIndicatorMode value);
    }
}
