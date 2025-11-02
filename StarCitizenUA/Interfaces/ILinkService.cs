using System.Threading;
using System.Threading.Tasks;

namespace StarCitizenUA.Interfaces
{
    public interface ILinkService
    {
        Task OpenLinkAsync(string url, CancellationToken cancellationToken = default);
    }
}
