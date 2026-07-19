using SCLOCVerse.Models.OcrPlatform;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Координатор OCR Platform — timer-driven цикл обробки регіонів.
    ///
    /// На кожному cycle (default 200мс = 5 Hz):
    /// 1. Перевірка StarCitizenForeground (skip якщо SC не активний)
    /// 2. Для кожного active region: capture → preprocess → OCR → validate
    /// 3. Publish OcrRegionReady event для підписників (Mining/Cargo/...)
    ///
    /// IDisposable — Timer.Dispose при зупинці.
    /// </summary>
    public interface IOcrCoordinator : IDisposable
    {
        /// <summary>
        /// Подія готовності результату для конкретного регіону.
        /// Підписники: MiningRecognitionService, CargoRecognitionService (future), ...
        /// </summary>
        event EventHandler<OcrRegionResult>? OcrRegionReady;

        /// <summary>Чи запущений цикл?</summary>
        bool IsRunning { get; }

        /// <summary>Запустити цикл (якщо ще не запущений). Idempotent.</summary>
        void Start();

        /// <summary>Зупинити цикл (якщо запущений). Idempotent.</summary>
        void Stop();
    }
}
