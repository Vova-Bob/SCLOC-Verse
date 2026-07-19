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
    }
}