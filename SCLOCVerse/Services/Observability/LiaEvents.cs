using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.LiaModels;
using SCLOCVerse.Models.Observability;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SCLOCVerse.Services.Observability
{
    /// <summary>
    /// Події встановлення L.I.A (Download / Install). Спільний ErrorContextExtractor;
    /// detail несе forensic-контекст (phase, hresult→signal, cert, activity_id, ...).
    /// </summary>
    public static class LiaEvents
    {
        public static void Track(
            ITelemetryService? telemetry,
            string operation,
            string outcome,
            long? durationMs = null,
            Exception? exception = null,
            string? orchestrationPhase = null,
            string? installerType = null,
            bool? certificatePresent = null,
            string? packageVersion = null,
            TelemetryLevel level = TelemetryLevel.Mandatory)
        {
            if (telemetry is null)
                return;

            try
            {
                TelemetryContext ctx = exception is not null
                    ? ErrorContextExtractor.Extract(exception) ?? new TelemetryContext()
                    : new TelemetryContext();

                if (durationMs.HasValue)
                    ctx.DurationMs = (int)durationMs.Value;

                // Для LiaInstallException ErrorContextExtractor уже встановив forensic-detail
                // (PowerShell-phase, signal, cert, activity_id). Для інших — доповнюємо orchestration.
                if (exception is not LiaInstallException)
                {
                    ctx.Detail ??= new Dictionary<string, object?>();
                    if (orchestrationPhase != null && !ctx.Detail.ContainsKey("phase"))
                        ctx.Detail["phase"] = orchestrationPhase;
                    ctx.Detail["retry_count"] = 0;
                    if (installerType != null)
                        ctx.Detail["installer_type"] = installerType;
                    if (certificatePresent.HasValue)
                        ctx.Detail["certificate_present"] = certificatePresent;
                    if (packageVersion != null)
                        ctx.Detail["package_version"] = packageVersion;
                }

                telemetry.Track("LIA", operation, outcome, ctx, level);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Telemetry] LiaEvents.Track failed: {ex.Message}");
            }
        }
    }
}