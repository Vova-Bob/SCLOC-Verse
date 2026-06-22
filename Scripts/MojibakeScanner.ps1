# This scanner detects CP1251-as-UTF8 mojibake in source files.
param(
    [string]$RepoPath = (Get-Location)
)

$ErrorActionPreference = 'Stop'

$extensions = @('*.cs','*.xaml','*.json','*.xml','*.config','*.settings','*.resx','*.md','*.txt','*.iss','*.ps1')

# Use regex for mojibake patterns
$pattern = 'Р[°±Ііґµ¶·ё№є»јЅѕї]|С[Ѓ‚ѓ„…†‡€‰‹ЊЌЋЏ‘]|Ð|Ñ||\?{3,}'

$findings = @()
foreach ($ext in $extensions) {
    Get-ChildItem -LiteralPath $RepoPath -Recurse -File -Filter $ext -ErrorAction SilentlyContinue | ForEach-Object {
        $path = $_.FullName
        $lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            if ($line -match $pattern) {
                $findings += [PSCustomObject]@{
                    File = $path.Substring($RepoPath.Length + 1)
                    Line = $i + 1
                    Text = $line.Trim()
                }
            }
        }
    }
}

$findings | Format-Table -AutoSize
Write-Host "Total findings: $($findings.Count)"
if ($findings.Count -gt 0) { exit 1 }
exit 0
