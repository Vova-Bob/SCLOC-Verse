using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Interfaces
{
    public interface IUpdateVerifier
    {
        Task<bool> VerifyAsync(
            string filePath,
            string? expectedChecksum,
            CancellationToken cancellationToken = default);
    }
}
