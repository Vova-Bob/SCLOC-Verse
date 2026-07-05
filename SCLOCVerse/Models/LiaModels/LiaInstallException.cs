namespace SCLOCVerse.Models.LiaModels
{
    /// <summary>
    /// Структурований виняток встановлення L.I.A — несе HRESULT, фазу, cert-контекст,
    /// ActivityId з PowerShell (через <see cref="LiaForensic"/> JSON-протокол).
    /// Успадковується від <see cref="System.InvalidOperationException"/> для зворотної
    /// сумісності (існуючі catch(InvalidOperationException) продовжують ловити — Zero Regression).
    /// </summary>
    public sealed class LiaInstallException : InvalidOperationException
    {
        public string? Hresult { get; }
        public string? Phase { get; }
        public string? ActivityId { get; }
        public string? AppxLog { get; }
        public int ExitCode { get; }
        public string? InstallerType { get; }
        public bool? CertificatePresent { get; }
        public string? CertificateSubject { get; }
        public string? CertificateThumbprint { get; }
        public string? ForensicJson { get; }

        public LiaInstallException(LiaForensic forensic, int exitCode, string rawError)
            : base(BuildMessage(forensic, rawError))
        {
            Hresult = forensic.Hresult;
            Phase = forensic.Phase;
            ActivityId = forensic.ActivityId;
            AppxLog = forensic.AppxLog;
            ExitCode = exitCode;
            InstallerType = forensic.InstallerType;
            CertificatePresent = forensic.CertificatePresent;
            CertificateSubject = forensic.CertificateSubject;
            CertificateThumbprint = forensic.CertificateThumbprint;
            ForensicJson = forensic.RawJson;
        }

        private static string BuildMessage(LiaForensic f, string rawError)
        {
            var hr = f.Hresult ?? "0x00000000";
            var phase = f.Phase ?? "unknown";
            var msg = string.IsNullOrWhiteSpace(f.Message) ? (rawError?.Trim() ?? string.Empty) : f.Message;
            return $"L.I.A install failed at {phase}: HRESULT {hr}. {msg}";
        }
    }
}