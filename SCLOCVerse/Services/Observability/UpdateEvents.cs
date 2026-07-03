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
            string? phase = null)
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
                    ctx ??= new TelemetryContext();
                    if (durationMs.HasValue)
                        ctx.DurationMs = (int)durationMs.Value;
                    if (phase != null)
                        ctx.Detail = new Dictionary<string, object?> { ["phase"] = phase, ["retry_count"] = 0 };
                }

                telemetry.Track("Updater", operation, outcome, ctx);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Telemetry] UpdateEvents.Track failed: {ex.Message}");
            }
        }
    }
}
