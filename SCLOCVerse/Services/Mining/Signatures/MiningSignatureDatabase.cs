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
            foreach (var (name, category, baseSig) in DefaultMiningSignatures.Materials)
            {
                if (raw % baseSig == 0)
                {
                    var cluster = raw / baseSig;
                    if (cluster is >= 1 and <= 20) // розумна межа
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
            if (_overrides is not null)
            {
                foreach (var (name, category, baseSig) in _overrides)
                {
                    if (raw % baseSig == 0)
                    {
                        var cluster = raw / baseSig;
                        if (cluster is >= 1 and <= 20)
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