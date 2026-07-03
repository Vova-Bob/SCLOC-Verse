using SCLOCVerse.Interfaces;
using SCLOCVerse.Services.Observability;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Services.ApplicationUpdate
{
    public class UpdateVerifier : IUpdateVerifier
    {
        private readonly ITelemetryService? _telemetry;

        public UpdateVerifier(ITelemetryService? telemetry = null)
        {
            _telemetry = telemetry;
        }

        public async Task<bool> VerifyAsync(
            string filePath,
            string? expectedChecksum,
            CancellationToken cancellationToken = default)
        {
            if (filePath is null)
                throw new ArgumentNullException(nameof(filePath));
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be empty.", nameof(filePath));

            cancellationToken.ThrowIfCancellationRequested();

            var sw = Stopwatch.StartNew();
            UpdateEvents.Track(_telemetry, "Verify", "Started");

            try
            {
                if (string.IsNullOrWhiteSpace(expectedChecksum))
                {
                    UpdateEvents.Track(_telemetry, "Verify", "Skipped", sw.ElapsedMilliseconds, phase: "NoChecksum");
                    return false;
                }

                if (!File.Exists(filePath))
                {
                    UpdateEvents.Track(_telemetry, "Verify", "Failed", sw.ElapsedMilliseconds, phase: "FileNotFound");
                    return false;
                }

                var trimmedChecksum = expectedChecksum.Trim();

                await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var sha256 = SHA256.Create();
                var hash = await sha256.ComputeHashAsync(fileStream, cancellationToken).ConfigureAwait(false);

                var actualChecksum = BitConverter.ToString(hash).Replace("-", string.Empty);

                var ok = string.Equals(actualChecksum, trimmedChecksum, StringComparison.OrdinalIgnoreCase);
                UpdateEvents.Track(_telemetry, "Verify", ok ? "Succeeded" : "Failed", sw.ElapsedMilliseconds,
                    phase: ok ? null : "ChecksumMismatch");
                return ok;
            }
            catch (Exception ex)
            {
                UpdateEvents.Track(_telemetry, "Verify", "Failed", sw.ElapsedMilliseconds, ex);
                throw; // Zero Regression.
            }
        }
    }
}
