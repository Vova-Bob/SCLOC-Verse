using System.Reflection;

namespace SCLOCVerse.Services.Observability
{
    /// <summary>
    /// Незмінні метадані поточного білда, що супроводжують кожну подію.
    /// Дозволяють знайти «усі помилки конкретного build» (Конституція, Стаття 9).
    /// </summary>
    public sealed class BuildInfo
    {
        /// <summary>Поточна версія формату payload телеметрії (Стаття 13/16).</summary>
        public const int TelemetryVersionCurrent = 1;

        public string AppVersion { get; }
        public string Channel { get; }
        public int TelemetryVersion { get; }
        public string? GitCommit { get; }

        public BuildInfo(string appVersion, string channel, int telemetryVersion, string? gitCommit)
        {
            AppVersion = appVersion;
            Channel = channel;
            TelemetryVersion = telemetryVersion;
            GitCommit = gitCommit;
        }

        /// <summary>Будує BuildInfo з поточної збірки. GitCommit поки не інжектиться (MSBuild-таргет — пізніший слайс).</summary>
        public static BuildInfo Create(string channel)
        {
            return new BuildInfo(ReadAppVersion(), channel, TelemetryVersionCurrent, gitCommit: null);
        }

        private static string ReadAppVersion()
        {
            try
            {
                var assembly = Assembly.GetEntryAssembly() ?? typeof(BuildInfo).Assembly;
                return assembly.GetName().Version?.ToString() ?? "0.0.0.0";
            }
            catch
            {
                return "0.0.0.0";
            }
        }
    }
}
