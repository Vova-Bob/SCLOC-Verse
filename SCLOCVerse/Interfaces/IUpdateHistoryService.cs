using SCLOCVerse.Models.ApplicationUpdate;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SCLOCVerse.Interfaces
{
    public interface IUpdateHistoryService
    {
        Task AddEntryAsync(UpdateHistoryEntry entry);
        Task<IReadOnlyList<UpdateHistoryEntry>> GetHistoryAsync();
        Task ClearAsync();
    }
}
