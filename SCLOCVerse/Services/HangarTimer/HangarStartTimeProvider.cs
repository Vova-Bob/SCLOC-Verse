using SCLOCVerse.Interfaces;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace SCLOCVerse.Services.HangarTimer
{
    /// <summary>
    /// Визначає час старту циклу Executive Hangar.
    /// Зберігає алгоритм старого StartTimeProvider без UI-залежностей.
    /// </summary>
    public class HangarStartTimeProvider : IHangarStartTimeProvider
    {
        private const string DefaultAppJsUrl = "https://exec.xyxyll.com/app.js";

        private const int DesignOnlineMin = 65;
        private const int DesignOfflineMin = 120;
        private const int CycleDriftMs = 226;
        private const int OffsetHours = -2;

        private readonly HttpClient _httpClient;
        private readonly IHangarSettingsService _settingsService;
        private long _cached;
        private DateTime _lastRemote = DateTime.MinValue;

        public HangarStartTimeProvider(HttpClient httpClient, IHangarSettingsService settingsService)
        {
            _httpClient = httpClient;
            _settingsService = settingsService;
        }

        public async Task<long?> ResolveAsync(bool forceRemote = false, CancellationToken cancellationToken = default)
        {
            if (forceRemote)
            {
                var remote = await TryComputeFromAppJsAsync(DefaultAppJsUrl, cancellationToken).ConfigureAwait(false);
                if (remote.HasValue) return _cached = remote.Value;

                return ResolveFallback();
            }

            var reg = _settingsService.GetCycleStartOverride();
            if (reg != 0) return _cached = reg;

            bool needRemote = (DateTime.UtcNow - _lastRemote) > TimeSpan.FromMinutes(5);
            if (needRemote)
            {
                var remote = await TryComputeFromAppJsAsync(DefaultAppJsUrl, cancellationToken).ConfigureAwait(false);
                _lastRemote = DateTime.UtcNow;
                if (remote.HasValue) return _cached = remote.Value;
            }
            else if (_cached > 0)
            {
                return _cached;
            }

            return ResolveFallback();
        }

        public Task<long?> ForceSyncAsync(CancellationToken cancellationToken = default)
            => ResolveAsync(forceRemote: true, cancellationToken);

        public void SetLocalOverride(long startMs)
        {
            _settingsService.SetCycleStartOverride(startMs);
            _cached = startMs;
        }

        public void ClearLocalOverride()
        {
            _settingsService.ClearCycleStartOverride();
            _cached = 0;
        }

        private long? ResolveFallback()
        {
            if (long.TryParse(Environment.GetEnvironmentVariable("VITE_TIME_START_CYCLE"), out var envMs))
                return _cached = envMs;

            return _cached = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private async Task<long?> TryComputeFromAppJsAsync(string url, CancellationToken cancellationToken)
        {
            try
            {
                if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    return null;

                string js = await _httpClient.GetStringAsync(url).ConfigureAwait(false);

                var match = Regex.Match(js, @"INITIAL_OPEN_TIME\s*=\s*new Date\('([^']+)'\)");
                if (!match.Success)
                    return null;

                if (!DateTimeOffset.TryParse(match.Groups[1].Value, out var initialOpenTime))
                    return null;

                return ComputeCycleStartMs(initialOpenTime.UtcDateTime, DateTime.UtcNow);
            }
            catch
            {
                return null;
            }
        }

        internal static long ComputeCycleStartMs(DateTime initialOpenTimeUtc, DateTime nowUtc)
        {
            long designCycleMs = (DesignOnlineMin + DesignOfflineMin) * 60L * 1000L;
            long cycleDuration = designCycleMs + CycleDriftMs;

            long openDuration = (long)Math.Round(cycleDuration * DesignOnlineMin * 60.0 * 1000.0 / designCycleMs);

            long deltaMs = (long)((nowUtc - initialOpenTimeUtc).TotalMilliseconds);
            long cycles = Math.Max(0, deltaMs / cycleDuration);
            DateTime lastOpenStart = initialOpenTimeUtc.AddMilliseconds(cycles * cycleDuration);

            if ((long)((nowUtc - lastOpenStart).TotalMilliseconds) > openDuration)
                lastOpenStart = lastOpenStart.AddMilliseconds(cycleDuration);

            DateTime correctedStart = lastOpenStart.AddHours(OffsetHours);

            return new DateTimeOffset(correctedStart, TimeSpan.Zero).ToUnixTimeMilliseconds();
        }
    }
}
