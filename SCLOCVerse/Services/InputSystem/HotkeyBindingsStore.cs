using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SCLOCVerse.Services.InputSystem
{
    /// <summary>
    /// JSON-persistence перевизначених жестів гарячих клавіш.
    /// Файл: %LOCALAPPDATA%\SCLOCVerse\hotkeys.json.
    /// Правила (погоджено Phase 0.5):
    ///   - override-only: зберігає лише перевизначені (CurrentGesture ≠ null).
    ///     Reset до default → запис видаляється.
    ///   - versioned: { "version": 1, "bindings": { "HangarTimer.ToggleOverlay": "Ctrl+F10", ... } }.
    ///   - відсутній entry = default; нова гаряча клавіша не ламає схему.
    /// </summary>
    public sealed class HotkeyBindingsStore
    {
        private const int CurrentVersion = 1;
        private const string FileName = "hotkeys.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly string _filePath;

        public HotkeyBindingsStore()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var directory = Path.Combine(localAppData, "SCLOCVerse");

            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            _filePath = Path.Combine(directory, FileName);
        }

        // Тестовий конструктор (явний шлях файлу).
        internal HotkeyBindingsStore(string filePath)
        {
            _filePath = filePath;
        }

        /// <summary>
        /// Завантажує всі перевизначення (custom + unassigned).
        /// </summary>
        public (Dictionary<string, HotkeyGesture> bindings, HashSet<string> unassigned) Load()
        {
            var emptyBindings = new Dictionary<string, HotkeyGesture>(StringComparer.Ordinal);
            var emptyUnassigned = new HashSet<string>(StringComparer.Ordinal);

            if (!File.Exists(_filePath))
                return (emptyBindings, emptyUnassigned);

            try
            {
                var json = File.ReadAllText(_filePath);
                var model = JsonSerializer.Deserialize<HotkeyBindingsModel>(json, JsonOptions)
                    ?? new HotkeyBindingsModel();

                var bindings = new Dictionary<string, HotkeyGesture>(StringComparer.Ordinal);
                if (model.Bindings != null)
                {
                    foreach (var (id, gestureStr) in model.Bindings)
                    {
                        if (HotkeyGestureString.TryParse(gestureStr, out var gesture))
                            bindings[id] = gesture;
                    }
                }

                var unassigned = new HashSet<string>(StringComparer.Ordinal);
                if (model.Unassigned != null)
                {
                    foreach (var id in model.Unassigned)
                        unassigned.Add(id);
                }

                return (bindings, unassigned);
            }
            catch
            {
                return (emptyBindings, emptyUnassigned);
            }
        }

        /// <summary>
        /// Зберігає перевизначення (override-only: custom bindings + unassigned IDs).
        /// </summary>
        public void Save(Dictionary<string, HotkeyGesture> bindings, HashSet<string> unassigned)
        {
            var model = new HotkeyBindingsModel
            {
                Version = CurrentVersion,
                Bindings = new Dictionary<string, string>(StringComparer.Ordinal),
                Unassigned = unassigned.Count > 0 ? unassigned.ToList() : null
            };

            foreach (var (id, gesture) in bindings)
            {
                if (gesture != default)
                    model.Bindings[id] = HotkeyGestureString.Serialize(gesture);
            }

            try
            {
                var json = JsonSerializer.Serialize(model, JsonOptions);
                File.WriteAllText(_filePath, json);
            }
            catch
            {
                // Помилка запису — не crash.
            }
        }

        private sealed class HotkeyBindingsModel
        {
            [JsonPropertyName("version")]
            public int Version { get; set; } = CurrentVersion;

            [JsonPropertyName("bindings")]
            public Dictionary<string, string>? Bindings { get; set; }

            [JsonPropertyName("unassigned")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public List<string>? Unassigned { get; set; }
        }
    }
}