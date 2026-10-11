$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Get-ChildItem (Join-Path $PSScriptRoot '*.ps1') | ForEach-Object {
    $parseErrors = $null
    $tokens = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -gt 0) { throw ($parseErrors | Out-String) }
}
Write-Output 'PASS: Windows scripts parsed.'
Push-Location $root
try {
    & (Join-Path $root 'artifacts/windows-tests/FFXILauncher.Desktop.Smoke.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Windows native smoke checks failed.' }
    & (Join-Path $PSScriptRoot 'windows-probe.ps1') -OutputDirectory (Join-Path $root 'artifacts/native-probe')
} finally { Pop-Location }
