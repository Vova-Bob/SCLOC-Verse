using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Observability;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SCLOCVerse.Services.Observability
{
    /// <summary>
    /// Події фаз оновлення SCLOC-Verse (Download / Verify / Install).
    /// Спільний <see cref="ErrorContextExtractor"/> для класифікації помилок (Стаття 12 — DRY);
    /// detail {phase, retry_count} — phase показує етап збою, retry_count=0 (схема під Retry Policy).
    /// </summary>
    public static class UpdateEvents
    {
        public static void Track(
            ITelemetryService? telemetry,
            string operation,
            string outcome,
            long? durationMs = null,
            Exception? exception = null,
            string? phase = null,
            TelemetryLevel level = TelemetryLevel.Mandatory)
        {
            if (telemetry is null)
                return;

            try
            {
                TelemetryContext? ctx = exception is not null
                    ? ErrorContextExtractor.Extract(exception)
                    : null;

                if (durationMs.HasValue || phase != null)
                {
                    // Signal fields обов'язкові для Failed (chk_telemetry_failed_has_signal).
                    // phase — найінформативніший signal для Failed-без-exception (FileNotFound, ChecksumMismatch, ...).
                    ctx ??= new TelemetryContext { Source = "CLR", ExceptionType = phase ?? "Condition" };
                    if (durationMs.HasValue)
                        ctx.DurationMs = (int)durationMs.Value;
                    if (phase != null)
                        ctx.Detail = new Dictionary<string, object?> { ["phase"] = phase, ["retry_count"] = 0 };
                }

                telemetry.Track("Updater", operation, outcome, ctx, level);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Telemetry] UpdateEvents.Track failed: {ex.Message}");
            }
        }
    }
}
