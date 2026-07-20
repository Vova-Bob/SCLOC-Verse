using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.OcrPlatform;
using System.Collections.Concurrent;

namespace SCLOCVerse.Services.OcrPlatform.Coordinator
{
    /// <summary>
    /// Thread-safe реалізація <see cref="IOcrRegionRegistry"/> через ConcurrentDictionary.
    /// </summary>
    public sealed class OcrRegionRegistry : IOcrRegionRegistry
    {
        private readonly ConcurrentDictionary<string, OcrRegion> _regions = new();

        /// <inheritdoc />
        public void Register(OcrRegion region)
        {
            ArgumentNullException.ThrowIfNull(region);
            _regions[region.Id] = region;
        }

        /// <inheritdoc />
        public void Unregister(string regionId)
        {
            ArgumentNullException.ThrowIfNull(regionId);
            _regions.TryRemove(regionId, out _);
        }

        /// <inheritdoc />
        public IReadOnlyList<OcrRegion> GetActiveRegions()
        {
            return _regions.Values.Where(r => r.Enabled).ToList().AsReadOnly();
        }

        /// <inheritdoc />
        public IReadOnlyList<OcrRegion> GetAllRegions()
        {
            return _regions.Values.ToList().AsReadOnly();
        }

        /// <inheritdoc />
        public void SetEnabled(string regionId, bool enabled)
        {
            if (_regions.TryGetValue(regionId, out var region))
            {
                _regions[regionId] = region with { Enabled = enabled };
            }
        }

        /// <inheritdoc />
        public void UpdateScreenRect(string regionId, System.Windows.Rect screenRect)
        {
            if (_regions.TryGetValue(regionId, out var region))
            {
                _regions[regionId] = region with { ScreenRect = screenRect };
            }
        }

        /// <inheritdoc />
        public void Clear()
        {
            _regions.Clear();
        }
    }
}
