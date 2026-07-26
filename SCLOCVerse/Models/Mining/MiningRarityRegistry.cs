using System.Collections.Generic;

namespace SCLOCVerse.Models.Mining
{
    /// <summary>
    /// Рівень рідкісності матеріалу Star Citizen.
    ///
    /// <para><b>Подвійна роль:</b></para>
    /// <list type="bullet">
    /// <item>UI (Overlay Scanner) — колір назви, рейтинг зірок, текстова мітка.</item>
    /// <item>Бізнес-логіка — <see cref="Services.Mining.Signatures.MiningSignatureDatabase.Lookup"/>
    ///     використовує <see cref="RarityInfo.MaxCluster"/> для відхилення
    ///     фізично неможливих комбінацій (напр. Savrilium × 5).</item>
    /// </list>
    ///
    /// <para>MaxCluster — Source of Truth Star Citizen: Legendary=2, Epic=3, Rare=4,
    /// Uncommon=5, Common=6. Для невідомих матеріалів = 0 (Lookup використовує fallback=20).</para>
    /// </summary>
    public enum MaterialRarity
    {
        /// <summary>Рідкість невідома (ROC/FPS/Salvage або material == null).</summary>
        Unknown = 0,

        /// <summary>Звичайний (сірий, ★☆☆☆☆, макс. 6×).</summary>
        Common = 1,

        /// <summary>Незвичайний (бірюзовий, ★★☆☆☆, макс. 5×).</summary>
        Uncommon = 2,

        /// <summary>Рідкісний (синій, ★★★☆☆, макс. 4×).</summary>
        Rare = 3,

        /// <summary>Епічний (фіолетовий, ★★★★☆, макс. 3×).</summary>
        Epic = 4,

        /// <summary>Легендарний (помаранчевий, ★★★★★, макс. 2×).</summary>
        Legendary = 5
    }

    /// <summary>
    /// Довідкові метадані рідкісності для UI: колір, зірки, максимальний кластер.
    /// </summary>
    public sealed record RarityInfo
    {
        public MaterialRarity Rarity { get; init; }
        public string DisplayName { get; init; } = string.Empty;

        /// <summary>Hex-колір WPF (напр. "#FF8C2A"). Без альфи-каналу не передавати.</summary>
        public string ColorHex { get; init; } = "#E8F3FF";

        /// <summary>Рейтинг у вигляді 5 зірок (★ заповнена / ☆ порожня).</summary>
        public string Stars { get; init; } = "☆☆☆☆☆";

        /// <summary>Максимальна кількість каменів у кластері (довідково).</summary>
        public int MaxCluster { get; init; }
    }

    /// <summary>
    /// Статичний довідник «Назва матеріалу → Рідкість».
    ///
    /// ПРИНЦИПИ:
    /// - **Читання лише для UI** — жодних змін у логіці розпізнавання.
    /// - **Source of Truth** = офіційна таблиця рідкісностей Star Citizen (26 матеріалів).
    /// - **Reuse First** — не дублює <see cref="Services.Mining.Signatures.DefaultMiningSignatures"/>;
    ///   останній відповідає за сигнатури, цей клас — за рідкість.
    /// - Повертає <see cref="Unknown"/> для невідомих назв (ROC/FPS/Salvage/ null).
    /// </summary>
    public static class MiningRarityRegistry
    {
        // ── Кольори (hex, WPF) ──
        private const string ColorLegendary = "#FF8C2A"; // помаранчевий
        private const string ColorEpic       = "#B04DE0"; // фіолетовий
        private const string ColorRare       = "#3F8FDB"; // синій
        private const string ColorUncommon   = "#3DD6A8"; // бірюзовий
        private const string ColorCommon     = "#9DA3A8"; // сірий
        private const string ColorUnknown    = "#E8F3FF"; // нейтральний білий

        // ── Зірки ──
        private const string StarsLegendary = "★★★★★";
        private const string StarsEpic      = "★★★★☆";
        private const string StarsRare      = "★★★☆☆";
        private const string StarsUncommon  = "★★☆☆☆";
        private const string StarsCommon    = "★☆☆☆☆";
        private const string StarsUnknown   = "☆☆☆☆☆";

        // ── Готові інфо-об'єкти ──
        private static readonly RarityInfo InfoLegendary = new()
        {
            Rarity = MaterialRarity.Legendary,
            DisplayName = "Легендарний",
            ColorHex = ColorLegendary,
            Stars = StarsLegendary,
            MaxCluster = 2
        };

        private static readonly RarityInfo InfoEpic = new()
        {
            Rarity = MaterialRarity.Epic,
            DisplayName = "Епічний",
            ColorHex = ColorEpic,
            Stars = StarsEpic,
            MaxCluster = 3
        };

        private static readonly RarityInfo InfoRare = new()
        {
            Rarity = MaterialRarity.Rare,
            DisplayName = "Рідкісний",
            ColorHex = ColorRare,
            Stars = StarsRare,
            MaxCluster = 4
        };

        private static readonly RarityInfo InfoUncommon = new()
        {
            Rarity = MaterialRarity.Uncommon,
            DisplayName = "Незвичайний",
            ColorHex = ColorUncommon,
            Stars = StarsUncommon,
            MaxCluster = 5
        };

        private static readonly RarityInfo InfoCommon = new()
        {
            Rarity = MaterialRarity.Common,
            DisplayName = "Звичайний",
            ColorHex = ColorCommon,
            Stars = StarsCommon,
            MaxCluster = 6
        };

        private static readonly RarityInfo InfoUnknown = new()
        {
            Rarity = MaterialRarity.Unknown,
            DisplayName = "—",
            ColorHex = ColorUnknown,
            Stars = StarsUnknown,
            MaxCluster = 0
        };

        /// <summary>
        /// Мапа «Назва матеріалу → RarityInfo». Офіційна таблиця Star Citizen (26 матеріалів).
        /// Порядок = рідкість. Незмінна після ініціалізації.
        ///
        /// Дубльовані ключі (Ouratite) додані тому, що
        /// <see cref="Services.Mining.Signatures.DefaultMiningSignatures"/> використовує
        /// варіант «Ouratite», тоді як у грі/документації іноді трапляється
        /// інше написання. Покриває обидва варіанти, щоб забарвлення точно спрацювало.
        /// </summary>
        private static readonly Dictionary<string, RarityInfo> ByName = new()
        {
            // 🟠 Legendary (макс. 2×)
            ["Quantainium"] = InfoLegendary,
            ["Stileron"]     = InfoLegendary,
            ["Savrilium"]    = InfoLegendary,

            // 🟣 Epic (макс. 3×)
            ["Ouratite"] = InfoEpic,
            ["Riccite"]  = InfoEpic,
            ["Lindinium"] = InfoEpic,

            // 🔵 Rare (макс. 4×)
            ["Beryl"]    = InfoRare,
            ["Taranite"] = InfoRare,
            ["Borase"]   = InfoRare,
            ["Gold"]     = InfoRare,
            ["Bexalite"] = InfoRare,

            // 🟢 Uncommon (макс. 5×)
            ["Laranite"]  = InfoUncommon,
            ["Aslarite"]  = InfoUncommon,
            ["Titanium"]  = InfoUncommon,
            ["Tungsten"]  = InfoUncommon,
            ["Agricium"]  = InfoUncommon,
            ["Torite"]    = InfoUncommon,

            // ⚪ Common (макс. 6×)
            ["Hephaestanite"] = InfoCommon,
            ["Tin"]           = InfoCommon,
            ["Quartz"]        = InfoCommon,
            ["Corundum"]      = InfoCommon,
            ["Copper"]        = InfoCommon,
            ["Silicon"]       = InfoCommon,
            ["Iron"]          = InfoCommon,
            ["Aluminum"]      = InfoCommon,
            ["Ice"]           = InfoCommon
        };

        /// <summary>
        /// Повертає метадані рідкісності за назвою матеріалу.
        /// Case-sensitive, проте стійкий до Registar: покриває всі 26 офіційних назв.
        /// Якщо матеріал невідомий (ROC/FPS/Salvage, новий override, null) — повертає <see cref="InfoUnknown"/>.
        /// </summary>
        public static RarityInfo Get(string? materialName)
        {
            if (string.IsNullOrWhiteSpace(materialName)) return InfoUnknown;
            return ByName.TryGetValue(materialName, out var info) ? info : InfoUnknown;
        }

        /// <summary>
        /// Повертає базову сигнатуру матеріалу (1 камінь) за назвою.
        ///
        /// Читає <see cref="Services.Mining.Signatures.DefaultMiningSignatures.Materials"/>
        /// — НЕ дублює логіку визначення ресурсу (<see cref="Services.Mining.Signatures.MiningSignatureDatabase"/>).
        /// Використовується ТІЛЬКИ в UI Overlay Scanner для відображення реальної
        /// сигнатури кластера (raw = base × cluster), коли <c>state.RawCode</c>
        /// у Discovery mode містить базову сигнатуру, яку знайшов OcrFullScanLocator.
        ///
        /// Повертає null для невідомих матеріалів (ROC/FPS/Salvage) — для них
        /// відображається <c>state.RawCode</c> як є.
        /// </summary>
        public static int? TryGetBaseSignature(string? materialName)
        {
            if (string.IsNullOrWhiteSpace(materialName)) return null;

            foreach (var (name, _, baseSig) in
                     Services.Mining.Signatures.DefaultMiningSignatures.Materials)
            {
                if (string.Equals(name, materialName, StringComparison.Ordinal))
                    return baseSig;
            }

            return null;
        }
    }
}
