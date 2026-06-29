namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Постачальник часу старту циклу Executive Hangar.
    /// </summary>
    public interface IHangarStartTimeProvider
    {
        /// <summary>
        /// Визначає час старту поточного циклу в Unix-мілісекундах.
        /// </summary>
        Task<long?> ResolveAsync(bool forceRemote = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// Примусова синхронізація з віддаленим джерелом.
        /// </summary>
        Task<long?> ForceSyncAsync(CancellationToken cancellationToken = default);
        /// <summary>
        /// Встановити локальний override старту циклу.
        /// </summary>
        void SetLocalOverride(long startMs);

        /// <summary>
        /// Очистити локальний override старту циклу.
        /// </summary>
        void ClearLocalOverride();
    }
}
