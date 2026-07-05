namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Сервіс системного трея.
    /// Інкапсулює H.NotifyIcon.TaskbarIcon та керує його життєвим циклом.
    /// Події повертаються в UI-потік виклику.
    /// </summary>
    public interface ITrayService : IDisposable
    {
        /// <summary>Чи активовано трей (видима іконка).</summary>
        bool IsInitialized { get; }

        /// <summary>Ініціалізувати tray-іконку та контекстне меню.</summary>
        void Initialize();

        /// <summary>Показати вікно та активувати його (з трея).</summary>
        void ShowMainWindow();

        /// <summary>Подія: користувач двічі клікнув по tray-іконці.</summary>
        event EventHandler ShowRequested;

        /// <summary>Подія: користувач натиснув "Перевірити оновлення".</summary>
        event EventHandler CheckUpdatesRequested;

        /// <summary>Подія: користувач натиснув "Вийти".</summary>
        event EventHandler ExitRequested;
    }
}
