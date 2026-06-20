using StarCitizenUA.Interfaces;
using StarCitizenUA.Models.ApplicationUpdate;
using System;
using System.Linq;

namespace StarCitizenUA.Services.ApplicationUpdate
{
    public class ReleaseChannelResolver : IReleaseChannelResolver
    {
        public UpdateChannel ResolveChannel(GitHubRelease release)
        {
            if (release == null)
                throw new ArgumentNullException(nameof(release));

            return ResolveFromRelease(release);
        }

        public bool IsChannelMatch(GitHubRelease release, UpdateChannel channel)
        {
            if (release == null)
                throw new ArgumentNullException(nameof(release));

            return ResolveChannel(release) == channel;
        }

        private static UpdateChannel ResolveFromRelease(GitHubRelease release)
        {
            return release.Prerelease ? UpdateChannel.Dev : UpdateChannel.Stable;
        }
    }
}
