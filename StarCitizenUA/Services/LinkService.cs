using StarCitizenUA.Interfaces;
using System.Diagnostics;

namespace StarCitizenUA.Services
{
    public class LinkService : ILinkService
    {
        public void OpenLink(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Не вдалося відкрити посилання.", ex);
            }
        }
    }
}
