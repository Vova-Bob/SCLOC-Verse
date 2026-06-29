using System.Windows;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Сервіс керування візуальним overlay Hangar Timer.
    /// </summary>
    public interface IHangarOverlayService
    {
        /// <summary>
        /// Відкрити або створити overlay та показати його.
        /// </summary>
        void Show(long cycleStartMs);

        /// <summary>
        /// Приховати overlay (якщо відкрито).
        /// </summary>
        void Hide();

        /// <summary>
        /// Перемкнути видимість overlay.
        /// </summary>
        void Toggle(long cycleStartMs);

        /// <summary>
        /// Закрити overlay та звільнити ресурси.
        /// </summary>
        void Close();

        /// <summary>
        /// Чи відкритий overlay зараз.
        /// </summary>
        bool IsOpen { get; }

        /// <summary>
        /// Оновити авторитетний час старту циклу у відкритому overlay.
        /// </summary>
        void UpdateCycleStart(long cycleStartMs);
    }
}
