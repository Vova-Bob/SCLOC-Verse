using SCLOCVerse.Interfaces;
using System;
using System.Text;

namespace SCLOCVerse.Services.ApplicationUpdate
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
            script.AppendLine("$logPath = Join-Path $PSScriptRoot 'update.log'");
            script.AppendLine("function Write-UpdateLog($message) {");
            script.AppendLine("    $timestamp = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffZ')");
            script.AppendLine("    $line = \"$timestamp | $message\"");
            script.AppendLine("    Add-Content -Path $logPath -Value $line -Encoding UTF8 -ErrorAction SilentlyContinue");
            script.AppendLine("}");
            script.AppendLine();
            script.AppendLine("Write-UpdateLog 'Script started'");
            script.AppendLine();
            script.AppendLine("$parentProcessId = $env:SCLOCVerse_PARENT_PID");
            script.AppendLine();
            script.AppendLine("if (-not [string]::IsNullOrWhiteSpace($parentProcessId)) {");
            script.AppendLine("    $parentPid = [int]$parentProcessId");
            script.AppendLine("    Write-UpdateLog \"Waiting for parent process $parentPid\"");
            script.AppendLine("    while (Get-Process -Id $parentPid -ErrorAction SilentlyContinue) {");
            script.AppendLine("        Start-Sleep -Milliseconds 500");
            script.AppendLine("    }");
            script.AppendLine("    Write-UpdateLog 'Parent process exited'");
            script.AppendLine("}");
            script.AppendLine();
            script.AppendLine($"$installerPath = '{safeInstallerPath}'");
            script.AppendLine($"$applicationExePath = '{safeApplicationExePath}'");
            script.AppendLine();
            script.AppendLine("$setupLogPath = Join-Path $PSScriptRoot 'setup.log'");
            script.AppendLine("$setupArguments = @(");
            script.AppendLine("    '/VERYSILENT',");
            script.AppendLine("    '/NORESTART',");
            script.AppendLine("    '/NOCANCEL',");
            script.AppendLine("    '/SP-',");
            script.AppendLine("    '/CLOSEAPPLICATIONS',");
            script.AppendLine("    \"/LOG=$setupLogPath\"");
            script.AppendLine(");");
            script.AppendLine();
            script.AppendLine("Write-UpdateLog \"Launching Setup.exe with arguments: $($setupArguments -join ' ')\"");
            script.AppendLine("$setupProcess = Start-Process -FilePath $installerPath -ArgumentList $setupArguments -Wait -PassThru");
            script.AppendLine("Write-UpdateLog \"Setup.exe finished with exit code $($setupProcess.ExitCode)\"");
            script.AppendLine();
            script.AppendLine("if ($setupProcess.ExitCode -ne 0) {");
            script.AppendLine("    exit $setupProcess.ExitCode");
            script.AppendLine("}");
            script.AppendLine();
            script.AppendLine("Write-UpdateLog \"Launching $applicationExePath\"");
            script.AppendLine("Start-Process -FilePath $applicationExePath");
            script.AppendLine();
            script.AppendLine("Write-UpdateLog 'Script finished'");
            script.AppendLine("Remove-Item -Path $PSCommandPath -Force -ErrorAction SilentlyContinue");

            return script.ToString();
        }
    }
}
