using SCLOCVerse.Models.HangarTimer;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Координатор модуля Hangar Timer.
    /// </summary>
    public interface IHangarTimerService
    {
        /// <summary>
        /// Подія, що спрацьовує коли авторитетний час старту циклу змінився.
        /// </summary>
        event EventHandler? CycleStartChanged;

        /// <summary>
        /// Чи відкритий overlay зараз.
        /// </summary>
        bool IsOverlayOpen { get; }

        /// <summary>
        /// Авторитетний час старту поточного циклу в мілісекундах UTC, або null, якщо ще не встановлено.
        /// </summary>
        long? CycleStartMs { get; }

        /// <summary>
        /// Поточний стан циклу Executive Hangar, або null, якщо цикл ще не ініціалізовано.
        /// </summary>
        HangarCycleInfo? GetCycleInfo();

        /// <summary>
        /// Перемкнути видимість overlay. При першому виклику виконується синхронізація часу.
        /// </summary>
        Task ToggleOverlayAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Примусова синхронізація часу з віддаленим джерелом.
        /// </summary>
        Task ForceSyncAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Очистити локальний override та синхронізуватися.
        /// </summary>
        Task ClearOverrideAndSyncAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Встановити старт циклу = поточний час.
        /// </summary>
        void SetStartNow();

        /// <summary>
        /// Відкрити діалог ручного вводу часу старту.
        /// </summary>
        void PromptManualStart();
    }
}
