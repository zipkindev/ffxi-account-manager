[CmdletBinding()]
param(
    [ValidateRange(1, 168)][int]$LookbackHours = 24,
    [ValidateRange(0, 120)][int]$WatchSeconds = 60,
    [string]$OutputDirectory = "$env:LOCALAPPDATA\FFXILauncher\diagnostics"
)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Run this report on the Windows PC where POL fails.' }

# Observe an ordinary launch without starting, resizing, focusing or killing anything.
# Do not collect account identities, credentials, command lines or screenshots.
$started = Get-Date
$issues = [System.Collections.Generic.List[string]]::new()
$samples = [System.Collections.Generic.List[object]]::new()
$configuration = $null
$dataRoot = Join-Path $env:LOCALAPPDATA 'FFXILauncher'
try {
    $saved = Get-Content -LiteralPath (Join-Path $dataRoot 'accounts.json') -Raw | ConvertFrom-Json
    $configuration = [pscustomobject]@{
        WindowerPath = $saved.WindowerPath
        PolPath = $saved.PolPath
        NormalizePolWindow = $saved.NormalizePolWindow
    }
} catch { $issues.Add('Launcher configuration could not be read: ' + $_.Exception.Message) }

$executables = @()
$graphicsFiles = @()
if ($null -ne $configuration) {
    foreach ($path in @($configuration.WindowerPath, $configuration.PolPath)) {
        if ([string]::IsNullOrWhiteSpace($path)) { continue }
        try {
            $file = Get-Item -LiteralPath $path
            $executables += [pscustomobject]@{
                Path = $file.FullName; Version = $file.VersionInfo.FileVersion
                Length = $file.Length; LastWriteUtc = $file.LastWriteTimeUtc
            }
        } catch { $issues.Add('Configured executable unavailable: ' + $path) }
    }
    if (-not [string]::IsNullOrWhiteSpace($configuration.PolPath)) {
        $polDirectory = Split-Path -Parent $configuration.PolPath
        foreach ($name in @('DDraw.dll', 'D3DImm.dll', 'D3D8.dll', 'D3D9.dll', 'dgVoodooCpl.exe', 'dgVoodoo.conf')) {
            $path = Join-Path $polDirectory $name
            if (-not (Test-Path -LiteralPath $path)) { continue }
            try {
                $file = Get-Item -LiteralPath $path
                $graphicsFiles += [pscustomobject]@{
                    Path = $file.FullName; Description = $file.VersionInfo.FileDescription
                    ProductVersion = $file.VersionInfo.ProductVersion
                    Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
                    Length = $file.Length; LastWriteUtc = $file.LastWriteTimeUtc
                }
            } catch { $issues.Add('Graphics file could not be inspected: ' + $path + ': ' + $_.Exception.Message) }
        }
    }
}

Write-Output "Watching the launcher, POL and Windower for $WatchSeconds seconds. Try your usual launch now."
$deadline = (Get-Date).AddSeconds($WatchSeconds)
do {
    $observed = Get-Date
    foreach ($process in @(Get-Process -Name pol, Windower, FFXILauncher.Desktop -ErrorAction SilentlyContinue)) {
        try {
            $samples.Add([pscustomobject]@{
                ObservedUtc = $observed.ToUniversalTime(); Name = $process.ProcessName
                ProcessId = $process.Id; StartedUtc = $process.StartTime.ToUniversalTime()
                HasMainWindow = ($process.MainWindowHandle -ne [IntPtr]::Zero)
                Responding = $process.Responding; Path = $process.Path
            })
        } catch { $issues.Add('Process inspection failed for PID ' + $process.Id + ': ' + $_.Exception.Message) }
        finally { $process.Dispose() }
    }
    if ((Get-Date) -ge $deadline) { break }
    Start-Sleep -Milliseconds 500
} while ($true)

$events = @()
$eventQuerySucceeded = $false
try {
    # Read named EventData fields rather than localized Event Viewer message text.
    $records = @(Get-WinEvent -FilterHashtable @{
        LogName = 'Application'; Id = @(1000, 1001, 1002, 1026)
        StartTime = $started.AddHours(-$LookbackHours)
    } -ErrorAction Stop)
    foreach ($record in $records) {
        $xml = [xml]$record.ToXml()
        $fields = [ordered]@{}
        $index = 0
        # Select nodes explicitly: PowerShell unwraps unnamed <Data> elements
        # into strings, which otherwise drops .NET Runtime exception text.
        foreach ($field in $xml.Event.EventData.SelectNodes('*')) {
            $key = $field.GetAttribute('Name')
            if ([string]::IsNullOrWhiteSpace($key)) { $key = 'Data' + $index }
            $fields[$key] = $field.InnerText
            $index++
        }
        if (($fields.Values -join "`n") -notmatch '(?i)(?:^|[\\/\s])(?:pol|windower|ffxilauncher(?:\.desktop)?)\.exe(?:$|[\s,])') { continue }
        $events += [pscustomobject]@{
            TimeUtc = $record.TimeCreated.ToUniversalTime(); EventId = $record.Id
            Provider = $record.ProviderName; RecordId = $record.RecordId; Fields = $fields
        }
    }
    $eventQuerySucceeded = $true
} catch {
    if ($_.FullyQualifiedErrorId -like 'NoMatchingEventsFound*') { $eventQuerySucceeded = $true }
    else { $issues.Add('Application event log could not be read: ' + $_.Exception.Message) }
}

$dumps = @()
$dumpRoot = Join-Path $env:LOCALAPPDATA 'CrashDumps'
if (Test-Path -LiteralPath $dumpRoot) {
    try {
        # Per-application WER settings can place POL dumps in a child folder.
        $dumps = @(Get-ChildItem -LiteralPath $dumpRoot -Filter '*.dmp' -File -Recurse -Depth 2 -ErrorAction Stop |
            Where-Object { $_.Name -match '^(pol|windower|FFXILauncher(?:\.Desktop)?)\.' -and $_.LastWriteTime -ge $started.AddHours(-$LookbackHours) } |
            Select-Object FullName, Length, LastWriteTimeUtc)
    } catch { $issues.Add('Crash dump directory could not be inspected: ' + $_.Exception.Message) }
}

$report = [ordered]@{
    StartedUtc = $started.ToUniversalTime(); FinishedUtc = (Get-Date).ToUniversalTime()
    LookbackHours = $LookbackHours; WatchSeconds = $WatchSeconds
    Configuration = $configuration; Executables = $executables; GraphicsWrapperFiles = $graphicsFiles
    ProcessSamples = $samples.ToArray(); EventQuerySucceeded = $eventQuerySucceeded
    Events = $events; CrashDumpFiles = $dumps; CollectionIssues = $issues.ToArray()
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$outputPath = Join-Path $OutputDirectory ('pol-crash-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.json')
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $outputPath -Encoding UTF8
Write-Output "Report saved: $outputPath"
Write-Output "Matching Windows events: $($events.Count). Collection issues: $($issues.Count)."
Write-Output 'No matching event does not rule out a crash. The report lists existing dump files but does not read them.'
