namespace SCLOCVerse.Interfaces
{
    public interface IToastService
    {
        Task ShowToastAsync(string message, int durationMs = 5000);
    }
}
