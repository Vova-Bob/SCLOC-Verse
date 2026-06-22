using SCLOCVerse.Interfaces;
using SCLOCVerse.Models.LiaModels;

namespace SCLOCVerse.Services.LiaServices
{
    public class UpdateCheckerService
    {
        private readonly IUpdater _updater;

        public UpdateCheckerService(IUpdater updater)
        {
            _updater = updater;
        }
        public async Task<(string Message, LiaStatusColor Color)> CheckForPendingUpdatesAsync()
        {
            var status = await _updater.GetStatusAsync().ConfigureAwait(false);
            return (status.Message, status.Color);
        }
    }
}