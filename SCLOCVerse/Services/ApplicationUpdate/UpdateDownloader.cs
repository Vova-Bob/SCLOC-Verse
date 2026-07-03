using SCLOCVerse.Interfaces;
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

                await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);

                UpdateEvents.Track(_telemetry, "Download", "Succeeded", sw.ElapsedMilliseconds);
                return filePath;
            }
            catch (Exception ex)
            {
                UpdateEvents.Track(_telemetry, "Download", "Failed", sw.ElapsedMilliseconds, ex);
                throw; // Zero Regression.
            }
        }
    }
}
