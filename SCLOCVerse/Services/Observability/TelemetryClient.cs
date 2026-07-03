using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Observability;
using System;
using System.Diagnostics;
using System.Threading;

namespace SCLOCVerse.Services.Observability
{
    /// <summary>
    /// Реалізація <see cref="ITelemetryService"/> — єдина точка входу спостережуваності.
    /// </summary>
    /// <remarks>
    /// Контракт (Конституція):
    ///  - <see cref="Track"/> синхронний, O(1), ніколи не кидає (Стаття 1);
    ///  - не блокує UI (Стаття 2);
    ///  - конструювання дешеве, не ламає startup (Стаття 3);
    ///  - через <see cref="PrivacySanitizer"/> (Стаття 4);
    ///  - kill-switch через enabled (Стаття 10).
    /// </remarks>
    public sealed class TelemetryClient : ITelemetryService, IDisposable
    {
        // Фонова відправка. Slice 1: 30 с. Pre-auth події залишаються в черзі до авторизації.
        private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(30);

        private readonly BuildInfo _buildInfo;
        private readonly string _installId;
        private readonly string _osVersion;
        private readonly bool _enabled;
        private readonly TraceContext _traceContext;
        private readonly TelemetryEventQueue _queue;
        private readonly TelemetryUploader _uploader;
        private readonly Timer _flushTimer;

        public TelemetryClient(ISupabaseClientFactory clientFactory, string installId, BuildInfo buildInfo, bool enabled)
        {
            _buildInfo = buildInfo ?? throw new ArgumentNullException(nameof(buildInfo));
            _installId = installId ?? string.Empty;
            _osVersion = SafeOsVersion();
            _enabled = enabled;

            _traceContext = new TraceContext();
            _queue = new TelemetryEventQueue();
            _uploader = new TelemetryUploader(clientFactory, _queue);

            if (_enabled)
            {
                _flushTimer = new Timer(OnFlushTick, null, FlushInterval, FlushInterval);
            }
            else
            {
                // Вимкнено — таймер не запускаємо (Стаття 10), але черга існує (no-op).
                _flushTimer = new Timer(_ => { }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }

        /// <inheritdoc/>
        public void Track(string component, string operation, string outcome, TelemetryContext? context = null)
        {
            try
            {
                if (!_enabled)
                    return;

                var evt = BuildEvent(component, operation, outcome, context);
                _queue.Enqueue(evt);
            }
            catch (Exception ex)
            {
                // Стаття 1: телеметрія ніколи не кидає у бізнес-код.
                Debug.WriteLine($"[Telemetry] Track failed: {ex.Message}");
            }
        }

        private TelemetryEvent BuildEvent(string component, string operation, string outcome, TelemetryContext? context)
        {
            return new TelemetryEvent
            {
                Id = Guid.NewGuid(),
                ClientEventId = Guid.NewGuid(),
                SessionId = _traceContext.SessionId,
                CorrelationId = _traceContext.CorrelationId,
                Step = _traceContext.NextStep(),
                InstallId = _installId,
                OccurredAt = DateTimeOffset.UtcNow,
                AppVersion = _buildInfo.AppVersion,
                GitCommit = _buildInfo.GitCommit,
                Channel = _buildInfo.Channel,
                TelemetryVersion = _buildInfo.TelemetryVersion,
                OsVersion = _osVersion,
                Component = component,
                Operation = operation,
                Outcome = outcome,
                Severity = context?.Severity ?? DefaultSeverity(outcome),
                Category = context?.Category ?? "Operational",
                Source = context?.Source,
                HttpStatus = context?.HttpStatus,
                Hresult = context?.Hresult,
                SupabaseCode = context?.SupabaseCode,
                ExceptionType = context?.ExceptionType,
                ErrorMessage = PrivacySanitizer.Sanitize(context?.ErrorMessage),
                DurationMs = context?.DurationMs,
                Detail = context?.Detail
            };
        }

        private static string DefaultSeverity(string outcome) => outcome switch
        {
            "Failed" => "Error",
            _ => "Info"
        };

        private void OnFlushTick(object? state)
        {
            // Fire-and-forget: uploader серіалізує flush через SemaphoreSlim і ловить усе сам.
            _ = _uploader.FlushAsync();
        }

        private static string SafeOsVersion()
        {
            try { return Environment.OSVersion.VersionString; }
            catch { return "unknown"; }
        }

        public void Dispose()
        {
            try { _flushTimer?.Dispose(); } catch { /* ignore */ }
            try { _uploader?.Dispose(); } catch { /* ignore */ }
            // Slice 1: події, що залишились у памʼяті на виході, не персистуються
            // (JSONL-durability — Slice 2).
        }
    }
}
