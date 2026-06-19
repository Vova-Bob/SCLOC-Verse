using System;
using System.Text.RegularExpressions;

namespace StarCitizenUA.Helpers
{
    public static class VersionParser
    {
        private static readonly Regex VersionRegex = new(
            @"^v?(?\d+)(\.(?\d+))?(\.(?\d+))?(\.(\d+))?(?:[-+].*)?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static Version Parse(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                throw new ArgumentException("Version input cannot be null or empty.", nameof(input));

            var sanitized = Sanitize(input);

            if (Version.TryParse(sanitized, out var version))
                return version;

            throw new ArgumentException($"Invalid version format: '{input}'.", nameof(input));
        }

        public static bool TryParse(string? input, out Version version)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                version = new Version(0, 0, 0, 0);
                return false;
            }

            var sanitized = Sanitize(input);

            return Version.TryParse(sanitized, out version!);
        }

        private static string Sanitize(string input)
        {
            var match = VersionRegex.Match(input.Trim());
            if (!match.Success)
                return input.Trim();

            var major = match.Groups["major"].Value;
            var minor = match.Groups["minor"].Success ? match.Groups["minor"].Value : "0";
            var build = match.Groups["build"].Success ? match.Groups["build"].Value : "0";
            var revision = match.Groups["revision"].Success ? match.Groups["revision"].Value : "0";

            return $"{major}.{minor}.{build}.{revision}";
        }
    }
}
