using System.Threading;
using System.Threading.Tasks;

namespace StarCitizenUA.Interfaces
{
    public interface IUpdateVerifier
    {
        Task<bool> VerifyAsync(
            string filePath,
            string? expectedChecksum,
            CancellationToken cancellationToken = default);
    }
}
