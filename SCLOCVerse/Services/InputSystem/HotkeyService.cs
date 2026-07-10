using SCLOCVerse.Services.InputSystem.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Services.InputSystem
{
    /// <summary>
    /// Центральний сервіс глобальних гарячих клавіш SCLOC-Verse.
    /// Керує бекендом і реєстраціями, ізолюючи споживачів від деталей вводу.
    /// </summary>
    public sealed class HotkeyService : IHotkeyService
    {
        private readonly IHotkeyBackend _backend;
        private readonly bool _diagnosticsEnabled;
        private readonly HotkeyBindingsStore? _bindingsStore;
        private readonly Lock _lock = new();
        private readonly Dictionary<HotkeyId, HotkeyDefinition> _definitionsById = [];
        private readonly Dictionary<HotkeyGesture, List<HotkeyDefinition>> _definitionsByGesture = [];
        // Регістр у порядку реєстрації — для детермінованого відображення в Settings Hub.
        private readonly List<HotkeyDefinition> _orderedDefinitions = [];
        private readonly HashSet<HotkeyId> _lastPressedIds = [];
        private bool _disposed;

        /// <summary>
        /// Створює сервіс гарячих клавіш із заданим бекендом.
        /// </summary>
        /// <param name="bindingsStore">Опціональний JSON-store перевизначених жестів (Phase 0.5).
        /// null — поведінка як раніше (без persistence). Additive, Zero Regression.</param>
        public HotkeyService(IHotkeyBackend backend, bool enableDiagnostics = false,
            HotkeyBindingsStore? bindingsStore = null)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _diagnosticsEnabled = enableDiagnostics;
            _bindingsStore = bindingsStore;
            _backend.GestureDetected += OnGestureDetected;

            // Key-up підтримують лише бекенди, що реалізують IKeyStateBackend (Raw Input).
            // RegisterHotKey не здатен повідомляти про відпускання клавіші, тому цей інтерфейс
            // не реалізує — фіктивної події KeyUp більше немає, що й усуває попередження CS0067.
            if (_backend is IKeyStateBackend keyState)
                keyState.KeyUp += OnKeyUp;
        }

        /// <inheritdoc/>
        public bool IsBackendInitialized => _backend.IsInitialized;

        /// <inheritdoc/>
        public void Register(HotkeyDefinition definition, HotkeyConflictPolicy policy = HotkeyConflictPolicy.Reject)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                // Phase 0.5: застосувати збережені перевизначення жестів (override-only).
                ApplyStoredBindings(definition);

                var gesture = definition.EffectiveGesture;

                if (_definitionsById.ContainsKey(definition.Id))
                    throw new InvalidOperationException($"Гарячу клавішу з ідентифікатором {definition.Id} вже зареєстровано.");

                bool conflictExists = _definitionsByGesture.TryGetValue(gesture, out var existingList)
                    && existingList.Count > 0
                    && existingList.Any(d => d.Enabled);

                if (conflictExists)
                {
                    if (existingList == null)
                        throw new InvalidOperationException("Внутрішня помилка: список конфліктів не ініціалізовано.");

                    switch (policy)
                    {
                        case HotkeyConflictPolicy.Reject:
                            throw new InvalidOperationException($"Комбінація {gesture} вже зареєстрована.");

                        case HotkeyConflictPolicy.Replace:
                            foreach (var existing in existingList.ToList())
                            {
                                _definitionsById.Remove(existing.Id);
                                existingList.Remove(existing);
                                LogEvent($"Конфлікт: замінено {existing.Id} на {definition.Id}");
                            }

                            if (existingList.Count == 0)
                                _definitionsByGesture.Remove(gesture);
                            break;

                        case HotkeyConflictPolicy.AllowPriority:
                            // Дозволяємо обидві реєстрації; вибір за пріоритетом відбувається при спрацюванні.
                            break;
                    }
                }

                _definitionsById[definition.Id] = definition;

                if (!_definitionsByGesture.TryGetValue(gesture, out var list))
                {
                    list = [];
                    _definitionsByGesture[gesture] = list;

                    // Реєструємо новий жест у бекенді лише коли він з'являється вперше.
                    if (_backend is RegisterHotkeyBackend registerBackend)
                        registerBackend.TryRegister(gesture);
                }

                list.Add(definition);
                _orderedDefinitions.Add(definition);

                LogEvent($"Зареєстровано гарячу клавішу {definition.Id} -> {gesture}");
            }
        }

        /// <inheritdoc/>
        public void Unregister(HotkeyId id)
        {
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                if (!_definitionsById.TryGetValue(id, out var definition))
                {
                    LogEvent($"Спроба скасувати неіснуючу гарячу клавішу {id}");
                    return;
                }

                var gesture = definition.EffectiveGesture;

                if (_definitionsByGesture.TryGetValue(gesture, out var list))
                {
                    list.Remove(definition);
                    if (list.Count == 0)
                    {
                        _definitionsByGesture.Remove(gesture);

                        if (_backend is RegisterHotkeyBackend registerBackend)
                            registerBackend.Unregister(gesture);
                    }
                }

                _definitionsById.Remove(id);
                _orderedDefinitions.Remove(definition);
                LogEvent($"Скасовано реєстрацію гарячої клавішу {id}");
            }
        }

        /// <inheritdoc/>
        public void SetEnabled(HotkeyId id, bool enabled)
        {
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                if (!_definitionsById.TryGetValue(id, out var definition))
                    return;

                definition.Enabled = enabled;
                LogEvent($"Змінено стан гарячої клавіші {id}: Enabled={enabled}");
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<HotkeyDefinition> GetDefinitions()
        {
            lock (_lock)
            {
                return _orderedDefinitions
                    .Where(d => d.VisibleInUi)
                    .ToList();
            }
        }

        /// <inheritdoc/>
        public RebindResult Rebind(HotkeyId id, HotkeyGesture gesture, HotkeyConflictPolicy policy,
            out HotkeyId conflictingId)
        {
            conflictingId = default;

            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                if (!_definitionsById.TryGetValue(id, out var definition))
                {
                    LogEvent($"Rebind: неіснуюча гаряча клавіша {id}");
                    return RebindResult.InvalidGesture;
                }

                // Unchanged: новий жест == поточний.
                if (gesture == definition.EffectiveGesture)
                    return RebindResult.Unchanged;

                // Конфлікт: новий жест зайнятий іншою enabled-дією.
                if (_definitionsByGesture.TryGetValue(gesture, out var existingList)
                    && existingList.Count > 0
                    && existingList.Any(d => d.Enabled && d.Id != id))
                {
                    var conflicting = existingList.FirstOrDefault(d => d.Enabled && d.Id != id);
                    conflictingId = conflicting!.Id;

                    if (policy != HotkeyConflictPolicy.Replace)
                        return RebindResult.Conflict;

                    // Replace: витісняємо всі конфліктуючі (їх CurrentGesture = null).
                    foreach (var other in existingList.ToList())
                    {
                        if (other.Id == id || !other.Enabled)
                            continue;

                        other.CurrentGesture = null;
                        existingList.Remove(other);
                        LogEvent($"Rebind Replace: витіснено {other.Id} з жеста {gesture}");
                    }

                    if (existingList.Count == 0)
                        _definitionsByGesture.Remove(gesture);
                }

                // Re-register: прибрати з旧ого gesture-списку.
                var oldGesture = definition.EffectiveGesture;
                if (_definitionsByGesture.TryGetValue(oldGesture, out var oldList))
                {
                    oldList.Remove(definition);
                    if (oldList.Count == 0)
                    {
                        _definitionsByGesture.Remove(oldGesture);
                        if (_backend is RegisterHotkeyBackend oldBackend)
                            oldBackend.Unregister(oldGesture);
                    }
                }

                // Оновити жест.
                definition.CurrentGesture = gesture;

                // Додати до нового gesture-списку + зареєструвати у бекенді.
                if (!_definitionsByGesture.TryGetValue(gesture, out var newList))
                {
                    newList = [];
                    _definitionsByGesture[gesture] = newList;

                    if (_backend is RegisterHotkeyBackend newBackend)
                    {
                        if (!newBackend.TryRegister(gesture))
                        {
                            // Win32 FAIL — відкотити CurrentGesture.
                            definition.CurrentGesture = oldGesture == definition.DefaultGesture ? null : oldGesture;
                            if (!_definitionsByGesture.TryGetValue(oldGesture, out var revertList))
                            {
                                revertList = [];
                                _definitionsByGesture[oldGesture] = revertList;
                                if (_backend is RegisterHotkeyBackend rb)
                                    rb.TryRegister(oldGesture);
                            }
                            revertList.Add(definition);
                            LogEvent($"Rebind FAIL: Win32 реєстрація {gesture} невдала");
                            return RebindResult.RegistrationFailed;
                        }
                    }
                }

                newList.Add(definition);

                // Phase 0.5: персистувати перевизначення (override-only).
                PersistBindings();

                LogEvent($"Rebind: {id} {oldGesture} -> {gesture}");
                return RebindResult.Success;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _backend.GestureDetected -= OnGestureDetected;
                if (_backend is IKeyStateBackend keyState)
                    keyState.KeyUp -= OnKeyUp;
                _backend.Dispose();
            }

            LogEvent("HotkeyService disposed");
        }

        /// <summary>
        /// Ініціалізує бекенд із заданим джерелом віконних повідомлень.
        /// </summary>
        public void InitializeBackend(IHotkeyMessageSource messageSource)
        {
            if (messageSource == null)
                throw new ArgumentNullException(nameof(messageSource));

            _backend.Initialize(messageSource);
            LogEvent($"Backend initialized: {_backend.GetType().Name}");

            // Реєструємо всі поточні жести в бекендах, які потребують явної реєстрації.
            lock (_lock)
            {
                if (_backend is RegisterHotkeyBackend registerBackend)
                {
                    foreach (var gesture in _definitionsByGesture.Keys)
                        registerBackend.TryRegister(gesture);
                }
            }
        }

        private void OnGestureDetected(object? sender, HotkeyGesture gesture)
        {
            HotkeyDefinition? target;
            bool alreadyPressed;

            lock (_lock)
            {
                if (_disposed)
                    return;

                if (!_definitionsByGesture.TryGetValue(gesture, out var list))
                    return;

                target = list
                    .Where(d => d.Enabled)
                    .OrderByDescending(d => d.Priority)
                    .FirstOrDefault();

                if (target == null)
                    return;

                alreadyPressed = _lastPressedIds.Contains(target.Id);

                if (target.SuppressAutoRepeat && alreadyPressed)
                {
                    LogDiagnostics("AutoRepeatSuppressed", $"id={target.Id} gesture={gesture}");
                    return;
                }

                _lastPressedIds.Add(target.Id);
            }

            LogEvent($"Спрацьовує гаряча клавіша {target.Id}");
            LogDiagnostics("HandlerInvoked", $"id={target.Id} gesture={gesture}");
            _ = ExecuteHandlerSafelyAsync(target.Handler);
        }

        private void OnKeyUp(object? sender, HotkeyGesture gesture)
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                if (!_definitionsByGesture.TryGetValue(gesture, out var list))
                    return;

                foreach (var definition in list)
                    _lastPressedIds.Remove(definition.Id);
            }

            LogDiagnostics("KeyUp", $"gesture={gesture}");
        }

        private static async Task ExecuteHandlerSafelyAsync(Func<CancellationToken, ValueTask> handler)
        {
            try
            {
                using var cts = new CancellationTokenSource();
                await handler(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ігноруємо скасування.
            }
            catch (Exception ex)
            {
                LogEvent($"Помилка обробника гарячої клавіші: {ex}");
            }
        }

        /// <summary>
        /// Завантажує збережені перевизначення жестів із store (override-only).
        /// Викликається при Register: якщо store має entry для id → CurrentGesture застосовується.
        /// </summary>
        private void ApplyStoredBindings(HotkeyDefinition definition)
        {
            if (_bindingsStore is null)
                return;

            var bindings = _bindingsStore.Load();
            if (bindings.TryGetValue(definition.Id.Value, out var storedGesture))
            {
                definition.CurrentGesture = storedGesture;
                LogEvent($"Rebind (restore): {definition.Id} -> {storedGesture} (з hotkeys.json)");
            }
        }

        /// <summary>
        /// Персистує поточні перевизначення (override-only: лише CurrentGesture ≠ null).
        /// Викликається після Rebind/Reset.
        /// </summary>
        private void PersistBindings()
        {
            if (_bindingsStore is null)
                return;

            var bindings = new Dictionary<string, HotkeyGesture>(StringComparer.Ordinal);
            foreach (var def in _definitionsById.Values)
            {
                if (def.CurrentGesture.HasValue && def.CurrentGesture.Value != def.DefaultGesture)
                    bindings[def.Id.Value] = def.CurrentGesture.Value;
            }

            _bindingsStore.Save(bindings);
        }

        private static void LogEvent(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[HotkeyService] {DateTime.Now:HH:mm:ss.fff} {message}");
        }

        private void LogDiagnostics(string source, string message)
        {
            if (_diagnosticsEnabled)
                InputDiagnostics.Write($"HotkeyService.{source}", message);
        }
    }
}
