namespace SCLOCVerse.Models.Mining
{
    /// <summary>
    /// Поточний стан mining-розпізнавання для overlay.
    /// Оновлюється MiningRecognitionService при нових OcrRegionReady подіях.
    /// </summary>
    public sealed class MiningState
    {
        /// <summary>Розпізнаний матеріал (null якщо не визначено).</summary>
        public MiningMaterial? Material { get; set; }

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
            return $"{Material.Name} | {fmt} | confidence={Confidence:F2}";
        }
    }
}