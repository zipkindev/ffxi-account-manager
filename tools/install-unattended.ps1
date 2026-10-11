param([Parameter(Mandatory=$true)][string]$ExpectedSid)
$ErrorActionPreference = 'Stop'
try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if ($identity.User.Value -ne $ExpectedSid) { throw 'Approve setup using the same Windows account that owns the saved credentials.' }
    if (-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Setup requires one administrator approval.' }
    $source = Split-Path $PSScriptRoot -Parent
    # Prepare a new protected package before switching the task; running binaries stay intact.
    $target = Join-Path $env:ProgramFiles ("FFXI Account Manager\" + $ExpectedSid + '-' + [Guid]::NewGuid().ToString('N'))
    $taskName = "FFXI Account Manager - $ExpectedSid"
    # A scheduled task must never execute an app from a user-writable Downloads folder.
    foreach ($directory in @((Split-Path $target -Parent), $target)) {
        if (Test-Path $directory) {
            if ((Get-Item $directory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Installation directory cannot be a link.' }
        } else { New-Item -ItemType Directory -Path $directory | Out-Null }
        $acl = New-Object Security.AccessControl.DirectorySecurity
        $acl.SetAccessRuleProtection($true, $false)
        $acl.SetOwner([Security.Principal.SecurityIdentifier]'S-1-5-32-544')
        foreach ($entry in @(@('S-1-5-18','FullControl'), @('S-1-5-32-544','FullControl'), @($ExpectedSid,'ReadAndExecute'))) {
            $rule = New-Object Security.AccessControl.FileSystemAccessRule ([Security.Principal.SecurityIdentifier]$entry[0]), $entry[1], 'ContainerInherit,ObjectInherit', 'None', 'Allow'
            $acl.AddAccessRule($rule)
        }
        Set-Acl -LiteralPath $directory -AclObject $acl
    }
    if (-not [string]::Equals($source, $target, [StringComparison]::OrdinalIgnoreCase)) {
        $files = @(Get-ChildItem -LiteralPath $source -Recurse -Force)
        if ($files | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'App package contains a link; install from an extracted release package.' }
        # Reject destination links before copying, including leftover links from an older install.
        if (Get-ChildItem -LiteralPath $target -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Installed package contains an unexpected link.' }
        foreach ($item in $files) {
            $relative = $item.FullName.Substring($source.Length).TrimStart('\')
            $destination = Join-Path $target $relative
            if ($item.PSIsContainer) { New-Item -ItemType Directory -Path $destination -Force | Out-Null }
            else { Copy-Item -LiteralPath $item.FullName -Destination $destination -Force }
        }
        # Strip explicit writable ACLs inherited from an earlier install.
        & "$env:SystemRoot\System32\icacls.exe" "$target\*" /reset /T /Q | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not protect the installed app files.' }
    }
    $exe = Join-Path $target 'FFXILauncher.Desktop.exe'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'The extracted Windows app executable is missing.' }
    $previous = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($previous) {
        $oldExecutable = $previous.Actions[0].Execute
        foreach ($manager in @(Get-Process -Name FFXILauncher.Desktop -ErrorAction SilentlyContinue)) {
            if ([string]::Equals($manager.Path, $oldExecutable, [StringComparison]::OrdinalIgnoreCase) -and
                $manager.MainWindowHandle -ne [IntPtr]::Zero -and $manager.MainWindowTitle -eq 'FFXI Account Manager') {
                # Close only the old task's manager UI, preserving independently running games.
                if (-not $manager.CloseMainWindow() -or -not $manager.WaitForExit(10000)) { throw 'Close the installed manager, then run setup again. Games can remain open.' }
            }
        }
    }
    $action = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $target
    $principal = New-ScheduledTaskPrincipal -UserId $ExpectedSid -LogonType Interactive -RunLevel Highest
    # Game children keep the previous scheduler instance alive after the UI closes.
    # Allow a replacement UI to start; the application's mutex still enforces one manager.
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances Parallel
    Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings -Force | Out-Null
    # Original user can read/start the task; only admins/SYSTEM can modify it.
    $scheduler = New-Object -ComObject 'Schedule.Service'
    $scheduler.Connect()
    $scheduler.GetFolder('\').GetTask($taskName).SetSecurityDescriptor("D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGX;;;$ExpectedSid)", 0)
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'FFXI Account Manager.lnk'))
    $shortcut.TargetPath = "$env:SystemRoot\System32\schtasks.exe"
    $shortcut.Arguments = '/Run /TN "' + $taskName + '"'
    $shortcut.WorkingDirectory = $target
    $shortcut.IconLocation = $exe
    $shortcut.WindowStyle = 7
    $shortcut.Save()
    # Update a previously deployed addon under this same approved elevation.
    # Fresh game launches load the new payload through the existing init entry.
    $configurationPath = Join-Path $env:LOCALAPPDATA 'FFXILauncher\accounts.json'
    try {
    if (Test-Path -LiteralPath $configurationPath) {
        $configuration = [IO.File]::ReadAllText($configurationPath) | ConvertFrom-Json
        $windower = $configuration.WindowerPath
        if ($windower -and [IO.Path]::GetFileName($windower) -eq 'Windower.exe' -and
            (Test-Path -LiteralPath (Join-Path (Split-Path $windower) 'addons\FFXIManager\FFXIManager.lua'))) {
            & $exe --deploy-addon $windower $ExpectedSid
            if ($LASTEXITCODE -ne 0) { throw 'App installed, but the existing FFXIManager addon update failed. Run Deploy addon from the updated manager.' }
        }
    }
    } catch { [Console]::Error.WriteLine('App installed; existing addon update needs attention. Run Deploy addon from the updated manager.') }
    Start-ScheduledTask -TaskName $taskName
    exit 0
} catch {
    # Setup errors contain paths/OS diagnostics only; no vault data is ever read here.
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
