using StarCitizenUA.Interfaces;

namespace StarCitizenUA.Services.LiaServices
{
    public class UpdateCheckerService
    {
        private readonly IUpdater _updater;

        public UpdateCheckerService(IUpdater updater)
        {
            _updater = updater;
        }

        public async Task<(string Message, System.Windows.Media.Brush Color)> CheckForPendingUpdatesAsync()
        {
            var status = await _updater.GetStatusAsync().ConfigureAwait(false);
            return (status.Message, status.Color);
        }
    }
}
