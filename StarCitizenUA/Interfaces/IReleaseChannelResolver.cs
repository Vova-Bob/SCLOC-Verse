using StarCitizenUA.Models.ApplicationUpdate;

namespace StarCitizenUA.Interfaces
{
    public interface IReleaseChannelResolver
    {
        UpdateChannel ResolveChannel(GitHubRelease release);
        bool IsChannelMatch(GitHubRelease release, UpdateChannel channel);
    }
}
