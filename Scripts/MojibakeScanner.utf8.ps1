param(
    [string]$RepoPath = (Get-Location),
    [string]$Scope = "production"
)

$ErrorActionPreference = 'Stop'

$extensions = @('*.cs','*.xaml','*.json','*.xml','*.config','*.settings','*.resx','*.iss','*.ps1')
$binaryExtensions = @('.png','.ico','.jpg','.jpeg','.gif','.bmp','.webp','.dll','.exe','.pdb','.db','.cache','.bin','.dat','.zip','.7z','.rar','.tar','.gz','.pdf','.doc','.docx','.xls','.xlsx','.mp3','.mp4','.avi','.mkv','.wav','.ogg')

# CP1251-as-UTF8 mojibake detection regex.
# Matches standalone CP1251 bytes that appear when Cyrillic text is mis-decoded as UTF-8.
$pattern = "\u0420[\u00b0\u00b1\u0406\u0456\u0491\u00b5\u00b6\u00b7\u0451\u2116\u0454\u00bb\u0458\u0405\u0455\u0457]|\u0421[\u0403\u201a\u0453\u201e\u2026\u2020\u2021\u20ac\u2030\u2039\u040a\u040c\u040b\u040f\u2018]|\u00d0|\u00d1|\u00a0|\uFFFD|\?\?\?+"

$ignoreFile = Join-Path $PSScriptRoot 'MojibakeScanner.ignore'
$ignorePatterns = @()
if (Test-Path $ignoreFile) {
    $ignorePatterns = Get-Content -LiteralPath $ignoreFile | Where-Object { $_ -and -not $_.StartsWith('#') }
}

function Test-ShouldIgnore($relativePath) {
    $lower = $relativePath.ToLowerInvariant()
    foreach ($ext in $binaryExtensions) {
        if ($lower.EndsWith($ext)) { return $true }
    }
    foreach ($p in $ignorePatterns) {
        $pattern = $p.Trim()
        if ([string]::IsNullOrWhiteSpace($pattern)) { continue }
        if ($relativePath -like $pattern) { return $true }
        $segments = $relativePath -split '[/\\]'
        foreach ($segment in $segments) {
            if ($segment -like $pattern) { return $true }
        }
    }
    return $false
}

$findings = @()

$searchRoots = @()
if ($Scope -eq 'production') {
    $searchRoots = @(
        (Join-Path $RepoPath 'SCLOCVerse'),
        (Join-Path $RepoPath 'Installer')
    )
} else {
    $searchRoots = @($RepoPath)
}

foreach ($root in $searchRoots) {
    if (-not (Test-Path $root)) { continue }
    foreach ($ext in $extensions) {
        Get-ChildItem -LiteralPath $root -Recurse -File -Filter $ext -ErrorAction SilentlyContinue | ForEach-Object {
            $path = $_.FullName
            $relativePath = $path.Substring($RepoPath.Length).TrimStart('\', '/')
            if (Test-ShouldIgnore $relativePath) { continue }

            $lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
            for ($i = 0; $i -lt $lines.Count; $i++) {
                $line = $lines[$i]
                # Use case-sensitive matching: CP1251 mojibake always starts with uppercase U+0420/U+0421,
                # while valid lowercase Ukrainian 'р' must not trigger a false positive.
                if ([regex]::IsMatch($line, $pattern)) {
                    $findings += [PSCustomObject]@{
                        File = $relativePath
                        Line = $i + 1
                        Text = $line.Trim()
                    }
                }
            }
        }
    }
}

$findings | Format-Table -AutoSize
Write-Host "Total findings: $($findings.Count)"
if ($findings.Count -gt 0) { exit 1 }
exit 0
