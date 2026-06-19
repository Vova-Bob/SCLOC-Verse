using Newtonsoft.Json;
using StarCitizenUA.Helpers;
using StarCitizenUA.Interfaces;
using StarCitizenUA.Models.ApplicationUpdate;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace StarCitizenUA.Services.ApplicationUpdate
{
    public class UpdateHistoryService : IUpdateHistoryService
    {
        private readonly string _historyFilePath;

        public UpdateHistoryService()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDirectory = Path.Combine(localAppData, UpdateConstants.UpdateDirectoryName);

            if (!Directory.Exists(appDirectory))
                Directory.CreateDirectory(appDirectory);

            _historyFilePath = Path.Combine(appDirectory, "update-history.json");
        }

        public async Task AddEntryAsync(UpdateHistoryEntry entry)
        {
            if (entry is null)
                throw new ArgumentNullException(nameof(entry));

            var history = await LoadHistoryAsync().ConfigureAwait(false);
            var updatedHistory = history.ToList();
            updatedHistory.Add(entry);

            var sortedHistory = updatedHistory
                .OrderByDescending(e => e.Timestamp)
                .ToList();

            await SaveHistoryAsync(sortedHistory).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<UpdateHistoryEntry>> GetHistoryAsync()
        {
            var history = await LoadHistoryAsync().ConfigureAwait(false);
            return history.AsReadOnly();
        }

        public async Task ClearAsync()
        {
            await Task.Run(() =>
            {
                if (File.Exists(_historyFilePath))
                    File.Delete(_historyFilePath);
            }).ConfigureAwait(false);
        }

        private async Task<List<UpdateHistoryEntry>> LoadHistoryAsync()
        {
            if (!File.Exists(_historyFilePath))
                return new List<UpdateHistoryEntry>();

            try
            {
                var json = await File.ReadAllTextAsync(_historyFilePath).ConfigureAwait(false);
                var history = JsonConvert.DeserializeObject<List<UpdateHistoryEntry>>(json);

                return history ?? new List<UpdateHistoryEntry>();
            }
            catch
            {
                return new List<UpdateHistoryEntry>();
            }
        }

        private async Task SaveHistoryAsync(List<UpdateHistoryEntry> history)
        {
            var json = JsonConvert.SerializeObject(history, Formatting.Indented);
            await File.WriteAllTextAsync(_historyFilePath, json).ConfigureAwait(false);
        }
    }
}
