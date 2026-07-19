using SCLOCVerse.Models.OcrPlatform;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Реєстр регіонів екрана, що цікавлять consumers (Mining/Cargo/Nav/...).
    /// Coordinator опитує всі активні регіони на кожному cycle.
    /// </summary>
    public interface IOcrRegionRegistry
    {
        /// <summary>Зареєструвати новий регіон. Якщо Id вже існує — замінити.</summary>
        void Register(OcrRegion region);

        /// <summary>Видалити регіон за Id. Якщо не існує — no-op.</summary>
        void Unregister(string regionId);

        /// <summary>Отримати всі активні регіони (Enabled=true).</summary>
        IReadOnlyList<OcrRegion> GetActiveRegions();

        /// <summary>Отримати всі зареєстровані регіони (включно з disabled).</summary>
        IReadOnlyList<OcrRegion> GetAllRegions();

        /// <summary>Активувати/деактивувати регіон без видалення (зручно для toggle в UI).</summary>
        void SetEnabled(string regionId, bool enabled);

        /// <summary>Очищення реєстру (використовується при Shutdown або reset).</summary>
        void Clear();
    }
}
