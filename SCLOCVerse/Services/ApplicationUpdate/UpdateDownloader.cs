using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.ApplicationUpdate;
using SCLOCVerse.Services.Observability;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Services.ApplicationUpdate
{
    public class UpdateDownloader : IUpdateDownloader
    {
        private readonly HttpClient _httpClient;
        private readonly ITelemetryService? _telemetry;

        public UpdateDownloader(HttpClient httpClient, ITelemetryService? telemetry = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _telemetry = telemetry;
        }

        public async Task<string> DownloadAsync(
            string downloadUrl,
            string targetDirectory,
            IProgress<UpdateDownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(downloadUrl))
                throw new ArgumentException("Download URL cannot be empty.", nameof(downloadUrl));
            if (string.IsNullOrWhiteSpace(targetDirectory))
                throw new ArgumentException("Target directory cannot be empty.", nameof(targetDirectory));

            if (!Directory.Exists(targetDirectory))
                Directory.CreateDirectory(targetDirectory);

            var fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("Could not determine file name from download URL.", nameof(downloadUrl));

            var filePath = Path.Combine(targetDirectory, fileName);

            // Спостережуваність: лише фактичне завантаження (арг-валідація вище — без подій).
            var sw = Stopwatch.StartNew();
            UpdateEvents.Track(_telemetry, "Download", "Started");

            try
            {
                using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength;
                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);

                await CopyWithProgressAsync(
                    contentStream,
                    fileStream,
                    totalBytes,
                    progress,
                    cancellationToken).ConfigureAwait(false);

                UpdateEvents.Track(_telemetry, "Download", "Succeeded", sw.ElapsedMilliseconds);
                return filePath;
            }
            catch (Exception ex)
            {
                UpdateEvents.Track(_telemetry, "Download", "Failed", sw.ElapsedMilliseconds, ex);
                throw; // Zero Regression.
            }
        }

        private static async Task CopyWithProgressAsync(
            Stream source,
            Stream destination,
            long? totalBytes,
            IProgress<UpdateDownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            const int BufferSize = 81920;
            const double MinimumReportIntervalSeconds = 0.2;

            var buffer = new byte[BufferSize];
            long downloadedBytes = 0;
            var stopwatch = Stopwatch.StartNew();
            var lastReportTime = TimeSpan.Zero;

            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                downloadedBytes += read;

                var elapsed = stopwatch.Elapsed;
                if (progress is not null && elapsed.TotalSeconds - lastReportTime.TotalSeconds >= MinimumReportIntervalSeconds)
                {
                    lastReportTime = elapsed;
                    var bytesPerSecond = elapsed.TotalSeconds > 0
                        ? downloadedBytes / elapsed.TotalSeconds
                        : (double?)null;

                    progress.Report(new UpdateDownloadProgress(downloadedBytes, totalBytes, bytesPerSecond));
                }
            }

            // Фінальний звіт після завершення читання.
            progress?.Report(new UpdateDownloadProgress(downloadedBytes, totalBytes, null));
        }
    }
}

