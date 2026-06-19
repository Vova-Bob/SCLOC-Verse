using StarCitizenUA.Models.ApplicationUpdate;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StarCitizenUA.Interfaces
{
    public interface IUpdateHistoryService
    {
        Task AddEntryAsync(UpdateHistoryEntry entry);
        Task<IReadOnlyList<UpdateHistoryEntry>> GetHistoryAsync();
        Task ClearAsync();
    }
}
