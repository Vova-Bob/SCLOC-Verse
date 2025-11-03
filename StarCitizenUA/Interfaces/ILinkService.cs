namespace StarCitizenUA.Interfaces
{
    public interface ILinkService
    {
        Task OpenLinkAsync(string url, CancellationToken cancellationToken = default);
    }
}
