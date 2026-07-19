using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.Mining;
using SCLOCVerse.Models.OcrPlatform;
using System.Diagnostics;

namespace SCLOCVerse.Services.Mining
{
    /// <summary>
    /// Реалізація <see cref="IMiningRecognitionService"/>:
    /// підписується на IOcrCoordinator.OcrRegionReady → lookup у IMiningSignatureDatabase →
    /// оновлює MiningState → raise StateChanged event.
    ///
    /// Регіони (registered через IOcrRegionRegistry):
    /// <list type="bullet">
    /// <item><c>mining.material_amount</c> — 5-значний код матеріалу (digits-only filter).</item>
    /// <item><c>mining.cluster_count</c> — кількість кластерів (digits-only filter).</item>
    /// </list>
    /// </summary>
    public sealed class MiningRecognitionService : IMiningRecognitionService
    {
        /// <summary>Region ID для коду матеріалу (5-значне число).</summary>
        public const string MaterialAmountRegionId = "mining.material_amount";

        /// <summary>Region ID для кількості кластерів.</summary>
        public const string ClusterCountRegionId = "mining.cluster_count";

        private readonly IMiningSignatureDatabase _database;
        private readonly IOcrCoordinator _coordinator;
        private readonly IOcrRegionRegistry _regionRegistry;
        private readonly object _stateLock = new();

        public MiningRecognitionService(
            IMiningSignatureDatabase database,
            IOcrCoordinator coordinator,
            IOcrRegionRegistry regionRegistry)
        {
            ArgumentNullException.ThrowIfNull(database);
            ArgumentNullException.ThrowIfNull(coordinator);
            ArgumentNullException.ThrowIfNull(regionRegistry);

            _database = database;
            _coordinator = coordinator;
            _regionRegistry = regionRegistry;
        }

        /// <inheritdoc />
        public MiningState CurrentState { get; } = new();

        /// <inheritdoc />
        public event EventHandler<MiningState>? StateChanged;

        /// <inheritdoc />
        public bool IsEnabled { get; private set; }

        /// <inheritdoc />
        public void Enable()
        {
            if (IsEnabled) return;
            _coordinator.OcrRegionReady += OnOcrRegionReady;
            RegisterDefaultRegions();
            IsEnabled = true;
            if (!_coordinator.IsRunning) _coordinator.Start();
        }

        /// <inheritdoc />
        public void Disable()
        {
            if (!IsEnabled) return;
            _coordinator.OcrRegionReady -= OnOcrRegionReady;
            UnregisterDefaultRegions();
            lock (_stateLock)
            {
                CurrentState.Material = null;
                CurrentState.RawCode = null;
                CurrentState.ClusterCount = null;
                CurrentState.Confidence = 0;
                CurrentState.LastUpdatedUtc = DateTime.UtcNow;
            }
            IsEnabled = false;
            StateChanged?.Invoke(this, CurrentState);
        }

        /// <summary>
        /// Handler для OcrRegionReady події — визначає тип регіону і оновлює state.
        /// </summary>
        private void OnOcrRegionReady(object? sender, OcrRegionResult e)
        {
            try
            {
                if (!IsEnabled) return;

                var text = e.Result.BestMatch?.Text;
                if (string.IsNullOrEmpty(text)) return;

                var confidence = e.Result.Confidence;

                lock (_stateLock)
                {
                    if (e.RegionId == MaterialAmountRegionId)
                    {
                        UpdateMaterial(text, confidence);
                    }
                    else if (e.RegionId == ClusterCountRegionId)
                    {
                        CurrentState.ClusterCount = text;
                        CurrentState.LastUpdatedUtc = e.CapturedAtUtc;
                        if (CurrentState.Confidence < confidence) CurrentState.Confidence = confidence;
                    }
                }

                StateChanged?.Invoke(this, CurrentState);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[MiningRecognitionService] OnOcrRegionReady exception: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Оновити material у state — lookup у database.
        /// </summary>
        private void UpdateMaterial(string code, double confidence)
        {
            var material = _database.Lookup(code);
            CurrentState.RawCode = code;
            CurrentState.Material = material;
            CurrentState.Confidence = confidence;
            CurrentState.LastUpdatedUtc = DateTime.UtcNow;
        }

        /// <summary>
        /// Зареєструвати стандартні регіони Mining Module.
        /// Координати визначаються через MiningRegionDefaults.GetForCurrentResolution()
        /// — auto-detect за height екрана (1080p / 1440p / 4K).
        /// </summary>
        private void RegisterDefaultRegions()
        {
            var (materialCode, clusterCount) = MiningRegionDefaults.GetForCurrentResolution();

            _regionRegistry.Register(new OcrRegion
            {
                Id = MaterialAmountRegionId,
                Name = "Mining Material Code",
                ScreenRect = materialCode,
                OcrOptions = OcrOptions.DigitsOnly
            });

            _regionRegistry.Register(new OcrRegion
            {
                Id = ClusterCountRegionId,
                Name = "Mining Cluster Count",
                ScreenRect = clusterCount,
                OcrOptions = OcrOptions.DigitsOnly
            });
        }

        private void UnregisterDefaultRegions()
        {
            _regionRegistry.Unregister(MaterialAmountRegionId);
            _regionRegistry.Unregister(ClusterCountRegionId);
        }
    }
}