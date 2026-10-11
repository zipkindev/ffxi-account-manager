$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/FFXILauncher.Desktop/FFXILauncher.Desktop.csproj'
$output = Join-Path $root 'artifacts/windows-x64'
dotnet publish $project -c Release -r win-x64 --self-contained true -o $output
if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
Write-Host "Run $output\FFXILauncher.Desktop.exe"
