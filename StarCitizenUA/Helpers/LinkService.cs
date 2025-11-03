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

        public async Task OpenLinkAsync(string url, CancellationToken cancellationToken = default)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                await _toastService.ShowToastAsync("Неприпустиме посилання. Перевірте URL.", 4000).ConfigureAwait(true);
                return;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                Process.Start(new ProcessStartInfo
                {
                    FileName = uri.AbsoluteUri,
                    UseShellExecute = true
                });
                await _toastService.ShowToastAsync("Відкриваємо посилання у браузері.", 3000).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[LinkService] Відкриття посилання скасовано користувачем.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LinkService] Не вдалося відкрити {uri.AbsoluteUri}: {ex}");
                await _toastService.ShowToastAsync($"Не вдалося відкрити посилання: {ex.Message}").ConfigureAwait(true);
            }
        }
    }
}
