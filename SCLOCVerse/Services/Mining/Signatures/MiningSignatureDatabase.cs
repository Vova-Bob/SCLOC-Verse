using Newtonsoft.Json;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Mining;
using System.IO;

namespace SCLOCVerse.Services.Mining.Signatures
{
    /// <summary>
    /// Математична реалізація <see cref="IMiningSignatureDatabase"/>.
    ///
    /// Принцип: signature = base × cluster.
    /// Відома лише сигнатура 1 камінця (base). Усі інші обчислюються:
    /// cluster = raw / base. Якщо raw ділиться на base без залишку — знайдено матеріал.
    ///
    /// Переваги:
    /// - Немає словника попередньо обчислених значень (мінімум пам'яті).
    /// - Підтримує будь-яку кількість кластерів (обчислення, а не lookup).
    /// - Новий матеріал = 1 рядок у Materials таблиці.
    ///
    /// JSON override: %LocalAppData%\SCLOCVerse\mining-signatures.json
    /// (дозволяє додавати матеріали без перекомпіляції).
    /// </summary>
    public sealed class MiningSignatureDatabase : IMiningSignatureDatabase
    {
        private readonly string? _overrideFilePath;
        private (string Name, string Category, int Base)[]? _overrides;

        public MiningSignatureDatabase(string? overrideFilePath = null)
        {
            _overrideFilePath = overrideFilePath ?? GetDefaultOverridePath();
            TryLoadOverrides();
        }

        /// <inheritdoc />
        public MiningMaterial? Lookup(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;

            // Нормалізувати: прибрати коми/пробіли → "3385".
            var normalized = code.Replace(",", "").Replace(" ", "").Replace(".", "");
            if (!int.TryParse(normalized, out var raw) || raw <= 0) return null;

            // 1. Спробувати матеріали (base × cluster).
            // MaxCluster — per-rarity ліміт з Source of Truth Star Citizen
            // (MiningRarityRegistry: Legendary=2, Epic=3, Rare=4, Uncommon=5, Common=6).
            // Якщо cluster перевищує max для цього матеріалу — кандидат відхиляється,
            // цикл продовжується (можливий перехід до LookupGeneric для ROC/FPS/Salvage).
            foreach (var (name, category, baseSig) in DefaultMiningSignatures.Materials)
            {
                if (raw % baseSig == 0)
                {
                    var cluster = raw / baseSig;
                    var maxCluster = GetMaxClusterOrFallback(name);
                    if (cluster >= 1 && cluster <= maxCluster)
                    {
                        return new MiningMaterial
                        {
                            Code = raw.ToString(),
                            Name = name,
                            Category = category,
                            ClusterFormat = $"Cluster: {cluster} Rocks",
                            IsRefineryInput = true
                        };
                    }
                }
            }

            // 2. Спробувати overrides (JSON).
            // Для невідомих матеріалів (не в MiningRarityRegistry) зберігається
            // стара поведінка з межею 20 (fallback).
            if (_overrides is not null)
            {
                foreach (var (name, category, baseSig) in _overrides)
                {
                    if (raw % baseSig == 0)
                    {
                        var cluster = raw / baseSig;
                        var maxCluster = GetMaxClusterOrFallback(name);
                        if (cluster >= 1 && cluster <= maxCluster)
                        {
                            return new MiningMaterial
                            {
                                Code = raw.ToString(),
                                Name = name,
                                Category = category,
                                ClusterFormat = $"Cluster: {cluster} Rocks",
                                IsRefineryInput = true
                            };
                        }
                    }
                }
            }

            // 3. Спробувати ROC/FPS/Salvage (фіксовані значення).
            return LookupGeneric(raw);
        }

        /// <summary>
        /// Повертає максимальний кластер для матеріалу (Source of Truth Star Citizen)
        /// з fallback для невідомих матеріалів (ROC/FPS/Salvage, JSON overrides).
        ///
        /// <para><b>Контракт:</b></para>
        /// <list type="bullet">
        /// <item>Відомий матеріал (26 офіційних): per-rarity ліміт (2/3/4/5/6).</item>
        /// <item>Невідомий матеріал: <paramref name="fallback"/> (за замовч. 20 — стара поведінка).</item>
        /// </list>
        ///
        /// <para>Використовується <see cref="Models.Mining.MiningRarityRegistry"/> —
        /// єдиним SSOT для per-rarity лімітів. Подвійного опису лімітів у коді немає.</para>
        /// </summary>
        private static int GetMaxClusterOrFallback(string? materialName, int fallback = 20)
        {
            var max = MiningRarityRegistry.Get(materialName).MaxCluster;
            return max > 0 ? max : fallback;
        }

        /// <inheritdoc />
        public int Count => DefaultMiningSignatures.Materials.Length + 3; // 26 + 3 generic

        /// <summary>
        /// Скинути + reload override (future use — Settings Hub button).
        /// </summary>
        public void Reload()
        {
            TryLoadOverrides();
        }

        private MiningMaterial? LookupGeneric(int raw)
        {
            // ROC
            var idx = System.Array.IndexOf(DefaultMiningSignatures.RocSignatures, raw);
            if (idx >= 0)
            {
                return new MiningMaterial
                {
                    Code = raw.ToString(),
                    Name = "ROC Mineable",
                    Category = "ROC",
                    ClusterFormat = $"Tier {idx + 1}",
                    IsRefineryInput = false
                };
            }

            // FPS
            idx = System.Array.IndexOf(DefaultMiningSignatures.FpsSignatures, raw);
            if (idx >= 0)
            {
                return new MiningMaterial
                {
                    Code = raw.ToString(),
                    Name = "FPS Mineable",
                    Category = "FPS",
                    ClusterFormat = $"Tier {idx + 1}",
                    IsRefineryInput = false
                };
            }

            // Salvage
            idx = System.Array.IndexOf(DefaultMiningSignatures.SalvageSignatures, raw);
            if (idx >= 0)
            {
                return new MiningMaterial
                {
                    Code = raw.ToString(),
                    Name = "Salvage",
                    Category = "Salvage",
                    ClusterFormat = $"Tier {idx + 1}",
                    IsRefineryInput = false
                };
            }

            return null;
        }

        private void TryLoadOverrides()
        {
            try
            {
                if (string.IsNullOrEmpty(_overrideFilePath) || !File.Exists(_overrideFilePath))
                {
                    return;
                }

                var json = File.ReadAllText(_overrideFilePath);
                var overrides = JsonConvert.DeserializeObject<(string Name, string Category, int Base)[]>(json);
                _overrides = overrides;
            }
            catch
            {
                // Silent — невалідний JSON не ламає built-in defaults.
            }
        }

        private static string GetDefaultOverridePath()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var dir = Path.Combine(appData, "SCLOCVerse");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "mining-signatures.json");
            }
            catch
            {
                return null!;
            }
        }
    }
}