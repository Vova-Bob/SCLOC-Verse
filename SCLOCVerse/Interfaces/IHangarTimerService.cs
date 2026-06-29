namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Координатор модуля Hangar Timer.
    /// </summary>
    public interface IHangarTimerService
    {
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
