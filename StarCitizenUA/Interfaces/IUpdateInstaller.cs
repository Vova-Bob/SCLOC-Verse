using System.Threading;
using System.Threading.Tasks;

namespace StarCitizenUA.Interfaces
{
    public interface IUpdateInstaller
    {
        Task<bool> InstallAsync(
            string installerPath,
            string applicationExePath,
            CancellationToken cancellationToken = default);
    }
}
