using SCLOCVerse.Controls;
using SCLOCVerse.Interfaces;
using SCLOCVerse.Services.InputSystem;
using System.Windows;

namespace SCLOCVerse.Services.HangarTimer
{
    /// <summary>
    /// Координатор модуля Hangar Timer.
    /// Не містить WinAPI, Overlay UI чи код гарячих клавіш — лише координує сервіси.
    /// </summary>
    public class HangarTimerService : IHangarTimerService, IDisposable
    {
        private readonly IHangarStartTimeProvider _startTimeProvider;
        private readonly IHangarOverlayService _overlayService;
        private readonly IHangarSettingsService _settingsService;
        private readonly IHotkeyService _hotkeyService;

        private long _cycleStartMs = -1;

        /// <inheritdoc/>
        public bool IsOverlayOpen => _overlayService.IsOpen;

        /// <inheritdoc/>
        public long? CycleStartMs => _cycleStartMs > 0 ? _cycleStartMs : null;

        public HangarTimerService(
            IHangarStartTimeProvider startTimeProvider,
            IHangarOverlayService overlayService,
            IHangarSettingsService settingsService,
            IHotkeyService hotkeyService)
        {
            _startTimeProvider = startTimeProvider;
            _overlayService = overlayService;
            _settingsService = settingsService;
            _hotkeyService = hotkeyService;

            RegisterHotkeys();
        }

        public async Task ToggleOverlayAsync(CancellationToken cancellationToken = default)
        {
            if (_overlayService.IsOpen)
            {
                _overlayService.Toggle(_cycleStartMs);
                return;
            }

            // Показуємо оверлей одразу з локальним часом, щоб уникнути затримки на першому запуску.
            if (_cycleStartMs <= 0)
                _cycleStartMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            _overlayService.Show(_cycleStartMs);

            // Синхронізуємо час у фоні та оновлюємо оверлей, якщо отримано нове значення.
            var start = await ResolveCycleStartAsync(forceRemote: false, cancellationToken).ConfigureAwait(false);
            if (start.HasValue && start.Value != _cycleStartMs)
            {
                _cycleStartMs = start.Value;
                _overlayService.UpdateCycleStart(_cycleStartMs);
            }
        }

        public async Task ForceSyncAsync(CancellationToken cancellationToken = default)
        {
            var start = await ResolveCycleStartAsync(forceRemote: true, cancellationToken).ConfigureAwait(false);
            if (!start.HasValue)
                return;

            _cycleStartMs = start.Value;
            _overlayService.UpdateCycleStart(_cycleStartMs);
        }

        public async Task ClearOverrideAndSyncAsync(CancellationToken cancellationToken = default)
        {
            _settingsService.ClearCycleStartOverride();
            await ForceSyncAsync(cancellationToken).ConfigureAwait(false);
        }

        public void SetStartNow()
        {
            var ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _startTimeProvider.SetLocalOverride(ms);
            _cycleStartMs = ms;
            _overlayService.UpdateCycleStart(_cycleStartMs);
        }

        public void PromptManualStart()
        {
            var owner = _overlayService is HangarOverlayService service ? service.GetWindow() : Application.Current.MainWindow;
            var dialog = new HangarManualStartDialog { Owner = owner };

            if (dialog.ShowDialog() == true)
            {
                _startTimeProvider.SetLocalOverride(dialog.ValueMs);
                _cycleStartMs = dialog.ValueMs;
                _overlayService.UpdateCycleStart(_cycleStartMs);
            }
        }

        public void Dispose()
        {
            _hotkeyService.Dispose();
            _overlayService.Close();
        }

        private async Task<long?> ResolveCycleStartAsync(bool forceRemote, CancellationToken cancellationToken)
        {
            var result = await _startTimeProvider.ResolveAsync(forceRemote, cancellationToken).ConfigureAwait(false);
            if (result.HasValue)
                return result.Value;

            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private void RegisterHotkeys()
        {
            _hotkeyService.Register(new HotkeyDefinition
            {
                Id = HotkeyIds.HangarToggleOverlay,
                DefaultGesture = new HotkeyGesture(HotkeyModifiers.None, HotkeyKey.F6),
                Description = "Показати/приховати Hangar overlay",
                Handler = async ct => await ToggleOverlayAsync(ct).ConfigureAwait(false)
            });

            _hotkeyService.Register(new HotkeyDefinition
            {
                Id = HotkeyIds.HangarToggleClickThrough,
                DefaultGesture = new HotkeyGesture(HotkeyModifiers.Shift, HotkeyKey.F8),
                Description = "Перемкнути кліки крізь Hangar overlay",
                Handler = ct =>
                {
                    if (_overlayService is HangarOverlayService svc)
                        svc.ToggleClickThrough();
                    return ValueTask.CompletedTask;
                }
            });

            _hotkeyService.Register(new HotkeyDefinition
            {
                Id = HotkeyIds.HangarBeginTemporaryDrag,
                DefaultGesture = new HotkeyGesture(HotkeyModifiers.Control, HotkeyKey.F8),
                Description = "Тимчасово перетягувати Hangar overlay",
                Handler = ct =>
                {
                    if (_overlayService is HangarOverlayService svc)
                        svc.BeginTemporaryDrag();
                    return ValueTask.CompletedTask;
                }
            });

            _hotkeyService.Register(new HotkeyDefinition
            {
                Id = HotkeyIds.HangarSetStartNow,
                DefaultGesture = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Shift, HotkeyKey.F7),
                Description = "Почати Hangar цикл зараз",
                Handler = ct =>
                {
                    SetStartNow();
                    return ValueTask.CompletedTask;
                }
            });

            _hotkeyService.Register(new HotkeyDefinition
            {
                Id = HotkeyIds.HangarPromptManualStart,
                DefaultGesture = new HotkeyGesture(HotkeyModifiers.Shift, HotkeyKey.F7),
                Description = "Ввести час старту Hangar циклу",
                Handler = ct =>
                {
                    PromptManualStart();
                    return ValueTask.CompletedTask;
                }
            });

            _hotkeyService.Register(new HotkeyDefinition
            {
                Id = HotkeyIds.HangarForceSync,
                DefaultGesture = new HotkeyGesture(HotkeyModifiers.None, HotkeyKey.F9),
                Description = "Синхронізувати Hangar цикл з URL",
                Handler = async ct => await ForceSyncAsync(ct).ConfigureAwait(false)
            });

            _hotkeyService.Register(new HotkeyDefinition
            {
                Id = HotkeyIds.HangarClearOverrideAndSync,
                DefaultGesture = new HotkeyGesture(HotkeyModifiers.Shift, HotkeyKey.F9),
                Description = "Стерти оверрайд Hangar і синхронізувати",
                Handler = async ct => await ClearOverrideAndSyncAsync(ct).ConfigureAwait(false)
            });

            _hotkeyService.Register(new HotkeyDefinition
            {
                Id = HotkeyIds.HangarScaleDown,
                DefaultGesture = new HotkeyGesture(HotkeyModifiers.Control, HotkeyKey.OemMinus),
                Description = "Зменшити масштаб Hangar overlay",
                Handler = ct =>
                {
                    if (_overlayService is HangarOverlayService svc)
                        svc.ScaleDown();
                    return ValueTask.CompletedTask;
                }
            });

            _hotkeyService.Register(new HotkeyDefinition
            {
                Id = HotkeyIds.HangarScaleUp,
                DefaultGesture = new HotkeyGesture(HotkeyModifiers.Control, HotkeyKey.OemPlus),
                Description = "Збільшити масштаб Hangar overlay",
                Handler = ct =>
                {
                    if (_overlayService is HangarOverlayService svc)
                        svc.ScaleUp();
                    return ValueTask.CompletedTask;
                }
            });

            _hotkeyService.Register(new HotkeyDefinition
            {
                Id = HotkeyIds.HangarScaleReset,
                DefaultGesture = new HotkeyGesture(HotkeyModifiers.Control, HotkeyKey.D0),
                Description = "Скинути масштаб Hangar overlay",
                Handler = ct =>
                {
                    if (_overlayService is HangarOverlayService svc)
                        svc.ScaleReset();
                    return ValueTask.CompletedTask;
                }
            });

            _hotkeyService.Register(new HotkeyDefinition
            {
                Id = HotkeyIds.HangarOpacityDown,
                DefaultGesture = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt, HotkeyKey.OemMinus),
                Description = "Зменшити прозорість Hangar overlay",
                Handler = ct =>
                {
                    if (_overlayService is HangarOverlayService svc)
                        svc.OpacityDown();
                    return ValueTask.CompletedTask;
                }
            });

            _hotkeyService.Register(new HotkeyDefinition
            {
                Id = HotkeyIds.HangarOpacityUp,
                DefaultGesture = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt, HotkeyKey.OemPlus),
                Description = "Збільшити прозорість Hangar overlay",
                Handler = ct =>
                {
                    if (_overlayService is HangarOverlayService svc)
                        svc.OpacityUp();
                    return ValueTask.CompletedTask;
                }
            });

            _hotkeyService.Register(new HotkeyDefinition
            {
                Id = HotkeyIds.HangarOpacityReset,
                DefaultGesture = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt, HotkeyKey.D0),
                Description = "Скинути прозорість Hangar overlay",
                Handler = ct =>
                {
                    if (_overlayService is HangarOverlayService svc)
                        svc.OpacityReset();
                    return ValueTask.CompletedTask;
                }
            });
        }
    }
}
