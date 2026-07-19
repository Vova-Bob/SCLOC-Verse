using SCLOCVerse.Models.Mining;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Сервіс розпізнавання mining з OcrRegionReady подій.
    /// Підписується на IOcrCoordinator.OcrRegionReady, робить lookup у IMiningSignatureDatabase,
    /// оновлює MiningState.
    /// </summary>
    public interface IMiningRecognitionService
    {
        /// <summary>Поточний стан mining-розпізнавання (read-only для consumers).</summary>
        MiningState CurrentState { get; }

        /// <summary>Подія оновлення стану (для MiningOverlayService підписки).</summary>
        event EventHandler<MiningState>? StateChanged;

        /// <summary>Чи активний модуль (запущено і виділяє ресурси).</summary>
        bool IsEnabled { get; }

        /// <summary>Увімкнути модуль (підписується на Coordinator events).</summary>
        void Enable();

        /// <summary>Вимкнути модуль (відписується, скидає state).</summary>
        void Disable();
    }
}