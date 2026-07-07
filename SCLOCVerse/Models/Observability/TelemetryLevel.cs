namespace SCLOCVerse.Models.Observability
{
    /// <summary>
    /// Рівень телеметрії (Phase 3.5 Telemetry Policy, KB §5.8).
    /// Відповідає на питання «коли відправляти?» — НЕ «що це за подія?» (це Category).
    /// Рішення приймає виключно TelemetryClient.Track() (KB §14.19 #118, #121).
    /// </summary>
    public enum TelemetryLevel
    {
        /// <summary>
        /// L1 — відправляється завжди (якщо не SCLOCVERSE_TELEMETRY_DISABLED).
        /// Статистика життя продукту: Failed термінальні, lifecycle Succeeded/Started.
        /// Gate null не блокує Mandatory (KB §14.19 #123).
        /// </summary>
        Mandatory,

        /// <summary>
        /// L2 — відправляється лише при AdvancedDiagnostics ON.
        /// Розширена діагностика: Started/Succeeded проміжні, forensic payload, duration_ms.
        /// Gate null → false → блокується (KB §14.19 #123).
        /// </summary>
        Diagnostic,

        /// <summary>
        /// L3 — ніколи не відправляється (локальний журнал).
        /// Зараз 0 використань; документує архітектуру (KB §14.19 #119).
        /// </summary>
        Local
    }
}