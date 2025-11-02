using StarCitizenUA.Interfaces;
using System.Diagnostics;

namespace StarCitizenUA.Helpers
{
    public class LinkService : ILinkService
    {
        private readonly IToastService _toastService;

        public LinkService(IToastService toastService)
        {
            _toastService = toastService;
        }

        public void OpenLink(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                _toastService.ShowToast("Відкриваємо посилання у браузері.");
            }
            catch (Exception ex)
            {
                _toastService.ShowToast($"Не вдалося відкрити посилання: {ex.Message}");
            }
        }
    }
}
