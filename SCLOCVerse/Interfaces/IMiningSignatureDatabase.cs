using SCLOCVerse.Models.Mining;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// База знань матеріалів Star Citizen для mining Module.
    /// Гібридна реалізація: built-in DefaultMiningSignatures (compiled) +
    /// JSON override (user-extensible, hot-reload через файл).
    /// </summary>
    public interface IMiningSignatureDatabase
    {
        /// <summary>
        /// Знайти матеріал за кодом-сигнатурою (зчитаним через OCR з HUD).
        /// </summary>
        /// <param name="code">Код (напр. "21425" = Aluminium).</param>
        /// <returns>Material якщо знайдено; null — невідомий код.</returns>
        MiningMaterial? Lookup(string code);

        /// <summary>Кількість унікальних матеріалів у базі.</summary>
        int Count { get; }
    }
}