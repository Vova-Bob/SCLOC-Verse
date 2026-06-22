using SCLOCVerse.Interfaces;

namespace SCLOCVerse.Services.ApplicationUpdate
{
    public class UpdateChannelService : IUpdateChannelService
    {
        public string GetUpdateChannel()
        {
            var channel = Settings.Default.UpdateChannel;
            return string.IsNullOrWhiteSpace(channel) ? "Stable" : channel;
        }

        public void SetUpdateChannel(string channel)
        {
            Settings.Default.UpdateChannel = string.IsNullOrWhiteSpace(channel) ? "Stable" : channel;
            Settings.Default.Save();
        }
    }
}
