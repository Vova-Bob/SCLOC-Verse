using System.Threading.Tasks;

namespace SCLOCVerse.Interfaces
{
    /// <summary>
    /// Показує UI діалоги аутентифікації.
    /// </summary>
    public interface IAuthDialogPresenter
    {
        Task ShowLoginPendingAsync();

        Task ShowProfileAsync();

        Task ShowErrorAsync(string message);
    }
}
