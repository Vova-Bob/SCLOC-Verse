using SCLOCVerse.Interfaces;
using System.Diagnostics;

namespace SCLOCVerse.Helpers
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
                await _toastService.ShowToastAsync("РќРµРїСЂРёРїСѓСЃС‚РёРјРµ РїРѕСЃРёР»Р°РЅРЅСЏ. РџРµСЂРµРІС–СЂС‚Рµ URL.", 4000).ConfigureAwait(true);
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
                await _toastService.ShowToastAsync("Р’С–РґРєСЂРёРІР°С”РјРѕ РїРѕСЃРёР»Р°РЅРЅСЏ Сѓ Р±СЂР°СѓР·РµСЂС–.", 3000).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[LinkService] Р’С–РґРєСЂРёС‚С‚СЏ РїРѕСЃРёР»Р°РЅРЅСЏ СЃРєР°СЃРѕРІР°РЅРѕ РєРѕСЂРёСЃС‚СѓРІР°С‡РµРј.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LinkService] РќРµ РІРґР°Р»РѕСЃСЏ РІС–РґРєСЂРёС‚Рё {uri.AbsoluteUri}: {ex}");
                await _toastService.ShowToastAsync($"РќРµ РІРґР°Р»РѕСЃСЏ РІС–РґРєСЂРёС‚Рё РїРѕСЃРёР»Р°РЅРЅСЏ: {ex.Message}").ConfigureAwait(true);
            }
        }
    }
}