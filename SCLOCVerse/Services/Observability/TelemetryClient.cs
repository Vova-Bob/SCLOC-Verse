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
    /// Розрив циклу залежностей auth ↔ telemetry: конструюється БЕЗ Supabase-клієнта
    /// (лише BuildInfo + enabled), тож може бути переданий у AuthService раніше за auth.
    /// InstallId та ClientFactory підключаються пізніше через <see cref="SetInstallId"/> /
    /// <see cref="AttachClientFactory"/> після побудови AuthCompositionRoot.
    ///
    /// Контракт (Конституція): Track sync/O(1)/non-throwing (Стаття 1), не блокує UI (Стаття 2),
    /// не ламає startup (Стаття 3), через PrivacySanitizer (Стаття 4), kill-switch (Стаття 10).
    /// </remarks>
    public sealed class TelemetryClient : ITelemetryService, IDisposable
    {
        // Фонова відправка. Slice 1: 30 с. Pre-auth події залишаються в черзі до авторизації.
        private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(30);

        private readonly BuildInfo _buildInfo;
        private string _installId;
        private readonly string _osVersion;
        private readonly bool _enabled;
        private readonly TraceContext _traceContext;
        private readonly TelemetryEventQueue _queue;

        private TelemetryUploader? _uploader;
        private Timer? _flushTimer;

        // Phase 3.5 Telemetry Policy (KB §5.8, §14.19 #118).
        // Two-phase init: TelemetryClient конструюється ДО IPreferencesService
        // (розрив циклу auth↔telemetry), тож gate підключається пізніше через
        // AttachDiagnosticGate — аналогічно SetInstallId / AttachClientFactory.
        // Єдиний споживач AdvancedDiagnostics (KB §14.19 #122).
        private Func<bool>? _diagnosticGate;

        // RC-401: при втраті авторизації (SignedOut) зупиняємо фонову відправку.
        // Track() продовжує працювати (кладе в чергу), але flush-timer мовчить.
        // Resume() — після повторної авторизації.
        private volatile bool _authStopped;

        public TelemetryClient(BuildInfo buildInfo, bool enabled)
        {
            _buildInfo = buildInfo ?? throw new ArgumentNullException(nameof(buildInfo));
            _installId = string.Empty; // встановлюється через SetInstallId після побудови auth.
            _osVersion = SafeOsVersion();
            _enabled = enabled;

            _traceContext = new TraceContext();
            _queue = new TelemetryEventQueue();
        }

        /// <summary>Підставляє анонімний ідентифікатор машини (install_id) після побудови auth.</summary>
        public void SetInstallId(string installId)
        {
            try { _installId = installId ?? string.Empty; }
            catch (Exception ex) { Debug.WriteLine($"[Telemetry] SetInstallId failed: {ex.Message}"); }
        }

        /// <summary>Підключає Supabase-клієнт і запускає фонову відправку (після побудови auth).</summary>
        public void AttachClientFactory(ISupabaseClientFactory clientFactory)
        {
            try
            {
                _uploader = new TelemetryUploader(clientFactory, _queue);
                if (_enabled)
                    _flushTimer = new Timer(OnFlushTick, null, FlushInterval, FlushInterval);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Telemetry] AttachClientFactory failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Підключає gate для TelemetryLevel.Diagnostic (Phase 3.5, KB §5.8, §14.19 #118).
        /// Two-phase init: викликається після побудови IPreferencesService.
        /// Gate null → Diagnostic блокується (conservative), Mandatory — ніколи (KB §14.19 #123).
        /// Єдиний споживач AdvancedDiagnostics (KB §14.19 #122).
        /// </summary>
        public void AttachDiagnosticGate(Func<bool> isDiagnosticEnabled)
        {
            try { _diagnosticGate = isDiagnosticEnabled; }
            catch (Exception ex) { Debug.WriteLine($"[Telemetry] AttachDiagnosticGate failed: {ex.Message}"); }
        }

        /// <inheritdoc/>
        public void Track(string component, string operation, string outcome, TelemetryContext? context = null, TelemetryLevel level = TelemetryLevel.Mandatory)
        {
            try
            {
                if (!_enabled)
                    return;

                // Phase 3.5 Telemetry Policy (KB §5.8, §14.19 #118, #121, #123).
                // Єдина точка прийняття рішення. Local — завжди блокується (ніколи не йде в БД).
                // Diagnostic — лише при gate ON. Gate null → false (conservative).
                // Mandatory — завжди проходить (gate null не блокує L1).
                if (level == TelemetryLevel.Local)
                    return;

                if (level == TelemetryLevel.Diagnostic && !IsDiagnosticEnabled())
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

        /// <summary>
        /// Gate перевірка для TelemetryLevel.Diagnostic (KB §14.19 #118, #123).
        /// Gate null → false (conservative: не відправляємо L2 до підключення gate).
        /// </summary>
        private bool IsDiagnosticEnabled()
        {
            try { return _diagnosticGate?.Invoke() ?? false; }
            catch { return false; }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Стаття 16 — Terminal Flush. Реалізація делегує в Uploader з Task.WhenAny+timeout.
        /// Non-throwing (Стаття 1): будь-яка помилка всередині логується й ковтається.
        /// </remarks>
        public async Task FlushAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (!_enabled || _uploader is null)
                return;

            try
            {
                var flushTask = _uploader.FlushAsync();
                var delayTask = Task.Delay(timeout, cancellationToken);
                var winner = await Task.WhenAny(flushTask, delayTask).ConfigureAwait(false);

                if (winner != flushTask)
                    Debug.WriteLine($"[Telemetry] FlushAsync timed out after {timeout.TotalSeconds:F1}s.");

                // Якщо flushTask устиг завершитись із винятком — ковтаємо (Стаття 1).
                // Не await'имо flushTask цілеспрямовано: у race з delayTask unobserved exception
                // логується всередині Uploader.FlushAsync власним try/catch.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Telemetry] FlushAsync failed: {ex.Message}");
            }
        }

        /// <inheritdoc/>
        public void Stop()
        {
            try { _authStopped = true; }
            catch (Exception ex) { Debug.WriteLine($"[Telemetry] Stop failed: {ex.Message}"); }
        }

        /// <inheritdoc/>
        public void Resume()
        {
            try { _authStopped = false; }
            catch (Exception ex) { Debug.WriteLine($"[Telemetry] Resume failed: {ex.Message}"); }
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
            // RC-401: при втраті авторизації (SignedOut) — не відправляємо.
            if (_authStopped) return;

            // Fire-and-forget: uploader серіалізує flush через SemaphoreSlim і ловить усе сам.
            _ = _uploader?.FlushAsync();
        }

        private static string SafeOsVersion()
        {
            try { return Environment.OSVersion.VersionString; }
            catch { return "unknown"; }
        }

        public void Dispose()
        {
            // Спочатку зупиняємо таймер, щоб він не стартував новий flush
            // під час фінального (race із завершенням застосунку).
            try { _flushTimer?.Dispose(); } catch { /* ignore */ }

            // Фінальний flush при закритті — fire-and-forget, без блокування UI-потоку.
            // Раніше тут був flushTask.Wait(5s), що гарантувано блокував UI на повні 5с:
            // Insert до Supabase зависав під час shutdown (Dispatcher гасне, auth-клієнт
            // у process-of-shutdown), а Wait вичерпував увесь таймаут. Це псувало UX
            // закриття застосунку (5+ секунд затримки після Tray → Вийти).
            //
            // Best-effort: запускаємо FlushAsync і не чекаємо. Uploader.FlushAsync має
            // внутрішній try/catch (Стаття 1 — non-throwing), тож unobserved exception
            // безпечний. Події, що не встигли відправитись за час життя процесу після
            // цього пункту, втрачаються — прийнятно для Operational-категорії телеметрії.
            // Під час роботи застосунку відправка йде через фоновий timer-flush (30с tick),
            // який не зачеплений цією зміною.
            if (_uploader is not null)
            {
                try
                {
                    _ = _uploader.FlushAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Telemetry] Final flush on dispose failed: {ex.Message}");
                }
            }

            try { _uploader?.Dispose(); } catch { /* ignore */ }
        }
    }
}
