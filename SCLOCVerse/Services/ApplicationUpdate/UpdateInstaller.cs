using SCLOCVerse.Interfaces;
using SCLOCVerse.Services.Observability;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SCLOCVerse.Services.ApplicationUpdate
{
    public class UpdateInstaller : IUpdateInstaller
    {
        private readonly IUpdateScriptBuilder _scriptBuilder;
        private readonly ITelemetryService? _telemetry;

        public UpdateInstaller(IUpdateScriptBuilder scriptBuilder, ITelemetryService? telemetry = null)
        {
            _scriptBuilder = scriptBuilder ?? throw new ArgumentNullException(nameof(scriptBuilder));
            _telemetry = telemetry;
        }

        public async Task<bool> InstallAsync(
            string installerPath,
            string applicationExePath,
            CancellationToken cancellationToken = default)
        {
            if (installerPath is null)
                throw new ArgumentNullException(nameof(installerPath));
            if (string.IsNullOrWhiteSpace(installerPath))
                throw new ArgumentException("Installer path cannot be empty.", nameof(installerPath));

            if (applicationExePath is null)
                throw new ArgumentNullException(nameof(applicationExePath));
            if (string.IsNullOrWhiteSpace(applicationExePath))
                throw new ArgumentException("Application path cannot be empty.", nameof(applicationExePath));

            cancellationToken.ThrowIfCancellationRequested();

            var sw = Stopwatch.StartNew();
            UpdateEvents.Track(_telemetry, "Install", "Started");

            try
            {
                if (!File.Exists(installerPath))
                {
                    UpdateEvents.Track(_telemetry, "Install", "Failed", sw.ElapsedMilliseconds, phase: "InstallerNotFound");
                    return false;
                }

                var updaterDirectory = Path.Combine(Path.GetTempPath(), "SCLOCVerse", "Updater");
                if (!Directory.Exists(updaterDirectory))
                    Directory.CreateDirectory(updaterDirectory);

                var scriptContent = _scriptBuilder.BuildScript(installerPath, applicationExePath);
                var scriptPath = Path.Combine(updaterDirectory, "update.ps1");

                await File.WriteAllTextAsync(scriptPath, scriptContent, cancellationToken).ConfigureAwait(false);

                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = updaterDirectory
                };

                startInfo.EnvironmentVariables["SCLOCVerse_PARENT_PID"] = Environment.ProcessId.ToString();

                using var process = Process.Start(startInfo);
                var launched = process != null;

                // Реальне завершення інсталяції відбувається у відокремленому процесі після shutdown
                // додатка — його результат фіксується bridge-event з історії оновлень (окремий слайс).
                // Тут фіксуємо лише запуск інсталятора.
                UpdateEvents.Track(_telemetry, "Install", launched ? "Succeeded" : "Failed", sw.ElapsedMilliseconds,
                    phase: launched ? "LauncherStarted" : "LaunchFailed");
                return launched;
            }
            catch (Exception ex)
            {
                UpdateEvents.Track(_telemetry, "Install", "Failed", sw.ElapsedMilliseconds, ex);
                throw; // Zero Regression.
            }
        }
    }
}
