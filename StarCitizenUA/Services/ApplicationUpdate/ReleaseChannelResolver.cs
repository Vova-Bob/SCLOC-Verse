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

            var tagName = release.TagName;
            if (string.IsNullOrWhiteSpace(tagName))
                throw new ArgumentException("TagName cannot be empty.", nameof(release));

            return ResolveFromTag(tagName);
        }

        public bool IsChannelMatch(GitHubRelease release, UpdateChannel channel)
        {
            if (release == null)
                throw new ArgumentNullException(nameof(release));

            return ResolveChannel(release) == channel;
        }

        private static UpdateChannel ResolveFromTag(string tagName)
        {
            var lowerTag = tagName.ToLowerInvariant();

            if (lowerTag.Contains("-experimental"))
                return UpdateChannel.Experimental;

            if (lowerTag.Contains("-dev"))
                return UpdateChannel.Dev;

            return UpdateChannel.Stable;
        }
    }
}
