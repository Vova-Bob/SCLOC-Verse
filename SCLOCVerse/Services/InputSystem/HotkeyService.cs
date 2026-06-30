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
        private readonly Lock _lock = new();
        private readonly Dictionary<HotkeyId, HotkeyDefinition> _definitionsById = [];
        private readonly Dictionary<HotkeyGesture, List<HotkeyDefinition>> _definitionsByGesture = [];
        private readonly HashSet<HotkeyId> _lastPressedIds = [];
        private bool _disposed;

        /// <summary>
        /// Створює сервіс гарячих клавіш із заданим бекендом.
        /// </summary>
        public HotkeyService(IHotkeyBackend backend, bool enableDiagnostics = false)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _diagnosticsEnabled = enableDiagnostics;
            _backend.GestureDetected += OnGestureDetected;
            _backend.KeyUp += OnKeyUp;
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
        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _backend.GestureDetected -= OnGestureDetected;
                _backend.KeyUp -= OnKeyUp;
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

        // Метод викликається з бекенду при key-up, якщо бекенд підтримує це.
        // RegisterHotKey не надсилає key-up; RawInput обробляє його всередині себе.
        internal void NotifyKeyUp(HotkeyGesture gesture)
        {
            lock (_lock)
            {
                if (!_definitionsByGesture.TryGetValue(gesture, out var list))
                    return;

                foreach (var definition in list)
                    _lastPressedIds.Remove(definition.Id);
            }
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
