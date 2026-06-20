using StarCitizenUA.Interfaces;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StarCitizenUA.Services.ApplicationUpdate
{
    public class UpdateInstaller : IUpdateInstaller
    {
        private readonly IUpdateScriptBuilder _scriptBuilder;

        public UpdateInstaller(IUpdateScriptBuilder scriptBuilder)
        {
            _scriptBuilder = scriptBuilder ?? throw new ArgumentNullException(nameof(scriptBuilder));
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

            if (!File.Exists(installerPath))
                return false;

            var updaterDirectory = Path.Combine(Path.GetTempPath(), "SCLocalizationUA", "Updater");
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

            startInfo.EnvironmentVariables["SCLocalizationUA_PARENT_PID"] = Environment.ProcessId.ToString();

            using var process = Process.Start(startInfo);
            return process != null;
        }
    }
}
