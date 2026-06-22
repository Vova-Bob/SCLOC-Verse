# This scanner detects CP1251-as-UTF8 mojibake in source files.
param([string]$RepoPath = (Get-Location))
$ErrorActionPreference = "Stop"
$extensions = @("*.cs","*.xaml","*.json","*.xml","*.config","*.settings","*.resx","*.md","*.txt","*.iss","*.ps1")
$pattern = "\u0420[\u00b0\u00b1\u0406\u0456\u0491\u00b5\u00b6\u00b7\u0451\u2116\u0454\u00bb\u0458\u0405\u0455\u0457]|\u0421[\u0403\u201a\u0453\u201e\u2026\u2020\u2021\u20ac\u2030\u2039\u040a\u040c\u040b\u040f\u2018]|\u00d0|\u00d1|\u00a0|\uFFFD|\?\?\?+"
$findings = @()
foreach ($ext in $extensions) {
    Get-ChildItem -LiteralPath $RepoPath -Recurse -File -Filter $ext -ErrorAction SilentlyContinue | ForEach-Object {
        $path = $_.FullName
        $lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            if ($line -match $pattern) {
                $findings += [PSCustomObject]@{
                    File = if ($path.Length -ge $RepoPath.Length) { $path.Substring($RepoPath.Length).TrimStart('\', '/') } else { $path }
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
