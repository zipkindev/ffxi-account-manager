$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'artifacts/windows-standalone'
dotnet publish (Join-Path $root 'src/FFXILauncher.Desktop') -c Release -r win-x64 --self-contained true -o $output -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -p:DebugType=embedded
if ($LASTEXITCODE -ne 0) { throw 'Standalone publish failed.' }
Get-ChildItem -LiteralPath $output -Filter '*.pdb' | Remove-Item
Write-Host "Standalone app: $output\FFXILauncher.Desktop.exe"
