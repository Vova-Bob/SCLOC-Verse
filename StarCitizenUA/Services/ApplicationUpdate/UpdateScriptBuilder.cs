using StarCitizenUA.Interfaces;
using System;
using System.Text;

namespace StarCitizenUA.Services.ApplicationUpdate
{
    public class UpdateScriptBuilder : IUpdateScriptBuilder
    {
        public string BuildScript(
            string installerPath,
            string applicationExePath)
        {
            if (installerPath is null)
                throw new ArgumentNullException(nameof(installerPath));
            if (string.IsNullOrWhiteSpace(installerPath))
                throw new ArgumentException("Installer path cannot be empty.", nameof(installerPath));

            if (applicationExePath is null)
                throw new ArgumentNullException(nameof(applicationExePath));
            if (string.IsNullOrWhiteSpace(applicationExePath))
                throw new ArgumentException("Application path cannot be empty.", nameof(applicationExePath));

            var safeInstallerPath = installerPath.Replace("'", "''");
            var safeApplicationExePath = applicationExePath.Replace("'", "''");

            var script = new StringBuilder();

            script.AppendLine("#Requires -Version 5.1");
            script.AppendLine();
            script.AppendLine("$ErrorActionPreference = 'Stop'");
            script.AppendLine();
            script.AppendLine("$parentProcessId = $env:SCLocalizationUA_PARENT_PID");
            script.AppendLine();
            script.AppendLine("if (-not [string]::IsNullOrWhiteSpace($parentProcessId)) {");
            script.AppendLine("    $parentPid = [int]$parentProcessId");
            script.AppendLine("    while (Get-Process -Id $parentPid -ErrorAction SilentlyContinue) {");
            script.AppendLine("        Start-Sleep -Milliseconds 500");
            script.AppendLine("    }");
            script.AppendLine("}");
            script.AppendLine();
            script.AppendLine($"$installerPath = '{safeInstallerPath}'");
            script.AppendLine($"$applicationExePath = '{safeApplicationExePath}'");
            script.AppendLine();
            script.AppendLine("$setupArguments = @(");
            script.AppendLine("    '/VERYSILENT',");
            script.AppendLine("    '/NORESTART',");
            script.AppendLine("    '/NOCANCEL',");
            script.AppendLine("    '/SP-',");
            script.AppendLine("    '/CLOSEAPPLICATIONS'");
            script.AppendLine(")");
            script.AppendLine();
            script.AppendLine("$setupProcess = Start-Process -FilePath $installerPath -ArgumentList $setupArguments -Wait -PassThru");
            script.AppendLine();
            script.AppendLine("if ($setupProcess.ExitCode -ne 0) {");
            script.AppendLine("    exit $setupProcess.ExitCode");
            script.AppendLine("}");
            script.AppendLine();
            script.AppendLine("Start-Process -FilePath $applicationExePath");
            script.AppendLine();
            script.AppendLine("Remove-Item -Path $PSCommandPath -Force -ErrorAction SilentlyContinue");

            return script.ToString();
        }
    }
}
