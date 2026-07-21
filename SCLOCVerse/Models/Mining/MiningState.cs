namespace SCLOCVerse.Models.Mining
{
    /// <summary>
    /// Поточний стан mining-розпізнавання для overlay.
    /// Оновлюється MiningRecognitionService при нових OcrRegionReady подіях.
    /// </summary>
    public sealed class MiningState
    {
        /// <summary>
        /// Список УСІХ кандидатів для поточної сигнатури.
        ///
        /// <para><b>Семантика:</b></para>
        /// <list type="bullet">
        /// <item>0 елементів — сигнатура невідома (<see cref="Material"/> = null).</item>
        /// <item>1 елемент — однозначний збіг (напр. Riccite × 2). <see cref="Material"/> = цей елемент.</item>
        /// <item>2-3 елементи — колізія ROC/FPS/Salvage (напр. 12000 → ROC T3 + FPS T4 + Salvage T6).
        /// <see cref="Material"/> = перший (ROC), але Overlay показує всі варіанти.</item>
        /// </list>
        ///
        /// <para><b>Контракт:</b> <see cref="Material"/> завжди дорівнює <c>AllCandidates.FirstOrDefault()</c>
        /// (якщо список непорожній), що зберігає зворотну сумісність для усіх наявних споживачів
        /// <see cref="Material"/> (Overlay, StateChanged, тощо).</para>
        /// </summary>
        public IReadOnlyList<MiningMaterial> AllCandidates { get; set; }
            = System.Array.Empty<MiningMaterial>();

        /// <summary>
        /// Перший (основний) розпізнаний матеріал (null якщо не визначено).
        ///
        /// <para>Зворотна сумісність: дорівнює <c>AllCandidates.FirstOrDefault()</c>.
        /// Для неколізійних сигнатур = єдиний кандидат; для колізійних = перший (ROC).</para>
        /// </summary>
        public MiningMaterial? Material => AllCandidates.Count > 0 ? AllCandidates[0] : null;

        /// <summary>Сирий код, розпізнаний OCR (напр. "21425").</summary>
        public string? RawCode { get; set; }

        /// <summary>Кількість кластерів (якщо вдалося розпізнати, напр. "5").</summary>
        public string? ClusterCount { get; set; }

        /// <summary>UTC timestamp останнього оновлення.</summary>
        public DateTime LastUpdatedUtc { get; set; }

        /// <summary>Confidence матеріалу (з ResultValidator, 0..1).</summary>
        public double Confidence { get; set; }

        public override string ToString()
        {
            if (Material is null) return "Mining: (no material)";
            var cluster = string.IsNullOrEmpty(ClusterCount) ? "?" : ClusterCount;
            var fmt = Material.ClusterFormat?.Replace("{0}", cluster) ?? $"Cluster: {cluster}";
            return AllCandidates.Count > 1
                ? $"{Material.Name} | {fmt} | +{AllCandidates.Count - 1} alt | confidence={Confidence:F2}"
                : $"{Material.Name} | {fmt} | confidence={Confidence:F2}";
        }
    }
}