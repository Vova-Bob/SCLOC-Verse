using SCLOCVerse.Interfaces;
using SCLOCVerse.Models;
using SCLOCVerse.Models.ApplicationUpdate;
using SCLOCVerse.Models.Notifications;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace SCLOCVerse.Services.Notifications
{
    /// <summary>
    /// Маршрутизатор сповіщень: перетворює UpdateCycleResult → NotificationCandidate[],
    /// виконує dedup через IPreferencesService, піднімає event для MainWindow.
    ///
    /// Чистий сервіс — не знає про WPF/Dispatcher. UI-маршалізацію виконує підписник.
    ///
    /// Backlog (post-Етап E): винести dedup в окремий INotificationHistoryService,
    /// коли з'являться інші канали (Discord, webhook тощо) — тоді єдина історія
    /// для всіх каналів, а не duplication per Router.
    /// </summary>
    public sealed class NotificationRouter : INotificationRouter
    {
        private readonly IPreferencesService _preferences;

        public NotificationRouter(IPreferencesService preferences)
        {
            _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        }

        public event EventHandler<IReadOnlyList<NotificationCandidate>>? NotificationsReady;

        public void Route(UpdateCycleResult result)
        {
            if (result is null)
                return;

            var candidates = new List<NotificationCandidate>();

            BuildAppCandidate(result, candidates);
            BuildLocalizationCandidates(result, candidates);
            BuildLiaCandidate(result, candidates);

            if (candidates.Count == 0)
                return;

            _preferences.SetLastToastTimestampUtc(DateTime.UtcNow.ToString("o"));
            NotificationsReady?.Invoke(this, candidates);
        }

        // --- Побудова кандидатів за джерелами ---

        private void BuildAppCandidate(UpdateCycleResult result, List<NotificationCandidate> candidates)
        {
            if (result.AppUpdate is not { IsUpdateAvailable: true, LatestVersion: { } version })
                return;

            var key = version.ToString();
            if (_preferences.GetLastAppToast() == key)
                return;

            candidates.Add(new NotificationCandidate(
                NotificationSource.Application,
                Environment: string.Empty,
                Version: key,
                Message: $"Доступна нова версія SCLOC-Verse {version}",
                Severity: ToastSeverity.Information,
                Policy: NotificationPolicy.Auto));

            _preferences.SetLastAppToast(key);
        }

        private void BuildLocalizationCandidates(UpdateCycleResult result, List<NotificationCandidate> candidates)
        {
            if (result.Localization.Count == 0)
                return;

            // Dedup per-environment у JSON: {"LIVE":"v2.1.15","PTU":"v2.1.16"}.
            // Стійкий до пошкодженого кешу — try/catch повертає порожній словник.
            var dedup = LoadLocalizationDedup();
            var changed = false;

            foreach (var loc in result.Localization)
            {
                var key = loc.Version ?? loc.Message;
                if (dedup.TryGetValue(loc.EnvironmentName, out var last) && last == key)
                    continue;

                candidates.Add(new NotificationCandidate(
                    NotificationSource.Localization,
                    Environment: loc.EnvironmentName,
                    Version: key,
                    Message: loc.Message,
                    Severity: ToastSeverity.Success,
                    Policy: NotificationPolicy.Auto));

                dedup[loc.EnvironmentName] = key;
                changed = true;
            }

            if (changed)
                SaveLocalizationDedup(dedup);
        }

        private void BuildLiaCandidate(UpdateCycleResult result, List<NotificationCandidate> candidates)
        {
            if (result.LiaStatus is not { IsUpdateAvailable: true, LatestVersion: { } version })
                return;

            var key = version.ToString();
            if (_preferences.GetLastLiaToast() == key)
                return;

            candidates.Add(new NotificationCandidate(
                NotificationSource.Lia,
                Environment: string.Empty,
                Version: key,
                Message: $"Доступна нова версія L.I.A {version}",
                Severity: ToastSeverity.Information,
                Policy: NotificationPolicy.Auto));

            _preferences.SetLastLiaToast(key);
        }

        // --- Dedup persistence (JSON) ---

        private Dictionary<string, string> LoadLocalizationDedup()
        {
            var json = _preferences.GetLastLocalizationToast();
            if (string.IsNullOrEmpty(json))
                return new Dictionary<string, string>();

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            }
            catch (JsonException)
            {
                // Пошкоджений кеш — починаємо з порожнього словника.
                return new Dictionary<string, string>();
            }
        }

        private void SaveLocalizationDedup(Dictionary<string, string> dedup)
            => _preferences.SetLastLocalizationToast(JsonSerializer.Serialize(dedup));
    }
}