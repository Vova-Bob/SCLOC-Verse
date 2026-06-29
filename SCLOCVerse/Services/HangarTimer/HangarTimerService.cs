using SCLOCVerse.Controls;
using SCLOCVerse.Interfaces;
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
        private readonly IHangarHotkeyService _hotkeyService;

        private long _cycleStartMs = -1;
        private bool _hotkeysSubscribed;

        public HangarTimerService(
            IHangarStartTimeProvider startTimeProvider,
            IHangarOverlayService overlayService,
            IHangarSettingsService settingsService,
            IHangarHotkeyService hotkeyService)
        {
            _startTimeProvider = startTimeProvider;
            _overlayService = overlayService;
            _settingsService = settingsService;
            _hotkeyService = hotkeyService;

            _hotkeyService.ActionRequested += OnHotkeyActionRequested;
            _hotkeysSubscribed = true;
        }

        public async Task ToggleOverlayAsync(CancellationToken cancellationToken = default)
        {
            if (_overlayService.IsOpen)
            {
                _overlayService.Toggle(_cycleStartMs);
                return;
            }

            var start = await ResolveCycleStartAsync(forceRemote: false, cancellationToken).ConfigureAwait(false);
            if (!start.HasValue)
                return;

            _cycleStartMs = start.Value;
            _overlayService.Show(_cycleStartMs);
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

        public void RegisterHotkeys(IntPtr windowHandle)
        {
            _hotkeyService.Register(windowHandle);
        }

        public void UnregisterHotkeys()
        {
            _hotkeyService.Unregister();
        }

        public void Dispose()
        {
            if (_hotkeysSubscribed)
            {
                _hotkeyService.ActionRequested -= OnHotkeyActionRequested;
                _hotkeysSubscribed = false;
            }

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

        private void OnHotkeyActionRequested(object? sender, HangarHotkeyAction e)
        {
            switch (e)
            {
                case HangarHotkeyAction.ToggleOverlay:
                    _ = ToggleOverlayAsync();
                    break;
                case HangarHotkeyAction.ToggleClickThrough:
                    if (_overlayService is HangarOverlayService svcClick)
                        svcClick.ToggleClickThrough();
                    break;
                case HangarHotkeyAction.BeginTemporaryDrag:
                    if (_overlayService is HangarOverlayService svcDrag)
                        svcDrag.BeginTemporaryDrag();
                    break;
                case HangarHotkeyAction.SetStartNow:
                    SetStartNow();
                    break;
                case HangarHotkeyAction.PromptManualStart:
                    PromptManualStart();
                    break;
                case HangarHotkeyAction.ForceSync:
                    _ = ForceSyncAsync();
                    break;
                case HangarHotkeyAction.ClearOverrideAndSync:
                    _ = ClearOverrideAndSyncAsync();
                    break;
                case HangarHotkeyAction.ScaleDown:
                    if (_overlayService is HangarOverlayService svcScaleDown)
                        svcScaleDown.ScaleDown();
                    break;
                case HangarHotkeyAction.ScaleUp:
                    if (_overlayService is HangarOverlayService svcScaleUp)
                        svcScaleUp.ScaleUp();
                    break;
                case HangarHotkeyAction.ScaleReset:
                    if (_overlayService is HangarOverlayService svcScaleReset)
                        svcScaleReset.ScaleReset();
                    break;
                case HangarHotkeyAction.OpacityDown:
                    if (_overlayService is HangarOverlayService svcOpacityDown)
                        svcOpacityDown.OpacityDown();
                    break;
                case HangarHotkeyAction.OpacityUp:
                    if (_overlayService is HangarOverlayService svcOpacityUp)
                        svcOpacityUp.OpacityUp();
                    break;
                case HangarHotkeyAction.OpacityReset:
                    if (_overlayService is HangarOverlayService svcOpacityReset)
                        svcOpacityReset.OpacityReset();
                    break;
            }
        }
    }
}
