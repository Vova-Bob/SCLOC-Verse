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
        /// Завантажує всі перевизначені прив'язки. Повертає порожній словник при
        /// відсутності файлу або помилці (fallback до default-жестів).
        /// </summary>
        public Dictionary<string, HotkeyGesture> Load()
        {
            if (!File.Exists(_filePath))
                return new Dictionary<string, HotkeyGesture>(StringComparer.Ordinal);

            try
            {
                var json = File.ReadAllText(_filePath);
                var model = JsonSerializer.Deserialize<HotkeyBindingsModel>(json, JsonOptions)
                    ?? new HotkeyBindingsModel();

                var result = new Dictionary<string, HotkeyGesture>(StringComparer.Ordinal);
                if (model.Bindings != null)
                {
                    foreach (var (id, gestureStr) in model.Bindings)
                    {
                        if (HotkeyGestureString.TryParse(gestureStr, out var gesture))
                            result[id] = gesture;
                        // Невалідний запис ігноруємо (не crash).
                    }
                }

                return result;
            }
            catch
            {
                // Пошкоджений файл → fallback (усі default). Не crash.
                return new Dictionary<string, HotkeyGesture>(StringComparer.Ordinal);
            }
        }

        /// <summary>
        /// Зберігає перевизначені прив'язки (override-only).
        /// </summary>
        public void Save(Dictionary<string, HotkeyGesture> bindings)
        {
            var model = new HotkeyBindingsModel
            {
                Version = CurrentVersion,
                Bindings = new Dictionary<string, string>(StringComparer.Ordinal)
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
                // Помилка запису — не crash (жесті лишається в пам'яті до наступного запуску).
            }
        }

        private sealed class HotkeyBindingsModel
        {
            [JsonPropertyName("version")]
            public int Version { get; set; } = CurrentVersion;

            [JsonPropertyName("bindings")]
            public Dictionary<string, string>? Bindings { get; set; }
        }
    }
}