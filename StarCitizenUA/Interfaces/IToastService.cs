
namespace StarCitizenUA.Interfaces
{
    public interface IToastService
    {
        void ShowToast(string message, int durationMs = 5000);
    }
}
