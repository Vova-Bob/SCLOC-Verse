using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Синхронізує метадані поточної інсталяції з Supabase.
    /// </summary>
    public interface IInstallationService
    {
        /// <summary>
        /// Унікальний ідентифікатор поточної інсталяції.
        /// </summary>
        string InstallId { get; }

        Task SyncCurrentInstallationAsync(CancellationToken cancellationToken = default);
    }
}
