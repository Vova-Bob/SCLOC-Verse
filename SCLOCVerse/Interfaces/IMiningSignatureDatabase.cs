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
        ///
        /// <para><b>Гібридна семантика:</b></para>
        /// <list type="bullet">
        /// <item>Якщо знайдено ОДИН кандидат — повертає його.</item>
        /// <item>Якщо знайдено ДЕКІЛЬКА кандидатів (колізія між ROC/FPS/Salvage) —
        /// повертає перший у фіксованому порядку (ROC → FPS → Salvage),
        /// тобто сумісний із застарілою поведінкою.</item>
        /// </list>
        ///
        /// <para>Щоб отримати всіх кандидатів при колізії, використовуйте <see cref="LookupAll"/>.</para>
        /// </summary>
        /// <param name="code">Код (напр. "21425" = Aluminum).</param>
        /// <returns>Material якщо знайдено; null — невідомий код.</returns>
        MiningMaterial? Lookup(string code);

        /// <summary>
        /// Знайти УСІ матеріали-кандидати за кодом-сигнатурою.
        ///
        /// <para><b>Повертає:</b></para>
        /// <list type="bullet">
        /// <item>0 елементів — сигнатура невідома.</item>
        /// <item>1 елемент — однозначний збіг (матеріал або єдиний generic).</item>
        /// <item>2-3 елементи — колізія між ROC/FPS/Salvage для спільних сигнатур
        /// (напр. 12000 → ROC Tier 3 + FPS Tier 5 + Salvage Tier 6).</item>
        /// </list>
        ///
        /// <para>Порядок: materials → overrides → ROC → FPS → Salvage.</para>
        /// </summary>
        /// <param name="code">Код (напр. "16000" → ROC Tier 4 + Salvage Tier 8).</param>
        /// <returns>Список кандидатів (може бути порожнім).</returns>
        IReadOnlyList<MiningMaterial> LookupAll(string code);

        /// <summary>Кількість унікальних матеріалів у базі.</summary>
        int Count { get; }
    }
}