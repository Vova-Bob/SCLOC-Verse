#Requires -Version 5.1

[CmdletBinding()]
param (
    [string]$InnoSetupCompiler = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    [string]$ProjectDir = "..\StarCitizenUA",
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = 'Stop'

$publishDir = Join-Path $ProjectDir "bin\$Configuration\net9.0-windows\$RuntimeIdentifier\publish"
$installerOutputDir = Join-Path $PSScriptRoot "Output"
$setupExeName = "SCLocalizationUA_Setup.exe"

function Test-InnoSetupCompiler {
    if (-not (Test-Path $InnoSetupCompiler)) {
        throw "Inno Setup compiler not found at: $InnoSetupCompiler"
    }
}

function Publish-Application {
    Write-Host "Publishing application..." -ForegroundColor Cyan
    $projectPath = Join-Path $ProjectDir "StarCitizenUA.csproj"
    $arguments = @(
        "publish",
        "$projectPath",
        "-c", $Configuration,
        "-r", $RuntimeIdentifier,
        "--self-contained",
        "-p:PublishSingleFile=true",
        "-p:PublishReadyToRun=true"
    )

    & dotnet @arguments

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    if (-not (Test-Path $publishDir)) {
        throw "Publish directory not found: $publishDir"
    }
}

function Build-Installer {
    Write-Host "Building installer with Inno Setup..." -ForegroundColor Cyan

    if (-not (Test-Path $installerOutputDir)) {
        New-Item -ItemType Directory -Path $installerOutputDir | Out-Null
    }

    $issPath = Join-Path $PSScriptRoot "SCLocalizationUA.iss"

    & $InnoSetupCompiler /O"$installerOutputDir" "$issPath"

    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
    }
}

function New-ChecksumFile {
    Write-Host "Generating SHA256 checksum file..." -ForegroundColor Cyan
    $setupPath = Join-Path $installerOutputDir $setupExeName

    if (-not (Test-Path $setupPath)) {
        throw "Installer not found: $setupPath"
    }

    $hash = (Get-FileHash -Path $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumFile = "$setupPath.sha256"
    $content = "$hash  $setupExeName"

    Set-Content -Path $checksumFile -Value $content -NoNewline -Encoding UTF8

    Write-Host "Created: $checksumFile" -ForegroundColor Green
}

try {
    Push-Location $PSScriptRoot
    Test-InnoSetupCompiler
    Publish-Application
    Build-Installer
    New-ChecksumFile

    Write-Host "Installer build completed successfully." -ForegroundColor Green
    Write-Host "Output: $installerOutputDir" -ForegroundColor Green
}
finally {
    Pop-Location
}
