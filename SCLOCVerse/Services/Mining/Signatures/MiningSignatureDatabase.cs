using Newtonsoft.Json;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Mining;
using System.IO;

namespace SCLOCVerse.Services.Mining.Signatures
{
    /// <summary>
    /// Гібридна реалізація <see cref="IMiningSignatureDatabase"/>:
    /// built-in DefaultMiningSignatures + JSON override з %LocalAppData%\SCLOCVerse\mining-signatures.json.
    ///
    /// JSON override дозволяє користувачеві додавати/змінювати матеріали без перекомпіляції
    /// (актуально при виході оновлень Star Citizen).
    /// </summary>
    public sealed class MiningSignatureDatabase : IMiningSignatureDatabase
    {
        private readonly Dictionary<string, MiningMaterial> _materials;
        private readonly string? _overrideFilePath;

        public MiningSignatureDatabase(string? overrideFilePath = null)
        {
            _overrideFilePath = overrideFilePath ?? GetDefaultOverridePath();
            _materials = new Dictionary<string, MiningMaterial>(DefaultMiningSignatures.Defaults);

            // Apply JSON overrides if file exists.
            TryLoadOverrides();
        }

        /// <inheritdoc />
        public MiningMaterial? Lookup(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            return _materials.TryGetValue(code, out var material) ? material : null;
        }

        /// <inheritdoc />
        public int Count => _materials.Count;

        /// <summary>
        /// Скинути на defaults + reload override (future use — Settings Hub button).
        /// </summary>
        public void Reload()
        {
            _materials.Clear();
            foreach (var (k, v) in DefaultMiningSignatures.Defaults)
            {
                _materials[k] = v;
            }
            TryLoadOverrides();
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
                var overrides = JsonConvert.DeserializeObject<Dictionary<string, MiningMaterial>>(json);
                if (overrides is null) return;

                foreach (var (code, material) in overrides)
                {
                    _materials[code] = material with { Code = code }; // гарантовано код
                }
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