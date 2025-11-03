namespace StarCitizenUA.Interfaces
{
    public interface IToastService
    {
        Task ShowToastAsync(string message, int durationMs = 5000);
    }
}
