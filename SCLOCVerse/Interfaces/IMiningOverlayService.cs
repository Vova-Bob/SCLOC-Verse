using SCLOCVerse.Models.Mining;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Сервіс керування Mining Overlay — lifecycle вікна + оновлення вмісту з MiningState.
    /// Незалежний життєвий цикл (як HangarOverlayService).
    /// </summary>
    public interface IMiningOverlayService
    {
        /// <summary>Показати overlay (якщо ще не показане).</summary>
        void Show();

        /// <summary>Приховати overlay.</summary>
        void Hide();

        /// <summary>Чи активне overlay?</summary>
        bool IsVisible { get; }

        /// <summary>Оновити вміст з нового MiningState.</summary>
        void UpdateState(MiningState state);

        /// <summary>
        /// Розташувати overlay під областю сканування (під HUD bounds).
        /// Викликається при кожному успішному розпізнаванні — overlay слідкує за HUD.
        /// </summary>
        /// <param name="hudBoundsX">X координата HUD bounds (пікселі екрана).</param>
        /// <param name="hudBoundsBottom">Y координата нижнього краю HUD bounds.</param>
        void SetPosition(double hudBoundsX, double hudBoundsBottom);

        /// <summary>Увімкнути тимчасовий режим перетягування (вимкне click-through).</summary>
        void BeginDrag();

        /// <summary>Збільшити прозорість overlay (+0.1, max 1.0).</summary>
        void IncreaseOpacity();

        /// <summary>Зменшити прозорість overlay (-0.1, min 0.2).</summary>
        void DecreaseOpacity();
    }
}