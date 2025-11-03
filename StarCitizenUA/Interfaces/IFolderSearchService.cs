using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StarCitizenUA.Interfaces
{
    public interface IFolderSearchService
    {
        Task<string?> FindOnFixedDrivesAsync(string targetFolderName, int maxDepth, CancellationToken cancellationToken);
        Task<string?> SearchDirectoryAsync(string rootDirectory, string targetFolderName, int maxDepth, CancellationToken cancellationToken);
        IEnumerable<string> EnumerateAccessibleDirectories(string rootDirectory);
    }
}
