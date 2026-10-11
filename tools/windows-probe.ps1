[CmdletBinding()]
param(
    [int]$ProcessId = 0,
    [string]$OutputDirectory = "$env:LOCALAPPDATA\FFXILauncher\calibration",
    [switch]$Capture
)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Run this probe on Windows.' }
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FfxiWindowProbe {
    public delegate bool EnumCallback(IntPtr window, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr window, out Rect rectangle);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
'@
$previousDpi = [FfxiWindowProbe]::SetThreadDpiAwarenessContext([IntPtr](-4))
try {
    $processes = @(Get-Process | Where-Object { $_.ProcessName -in @('pol', 'Windower') -and ($ProcessId -eq 0 -or $_.Id -eq $ProcessId) })
    $windows = [System.Collections.Generic.List[object]]::new()
    $callback = [FfxiWindowProbe+EnumCallback]{
        param($handle, $parameter)
        [uint32]$ownerId = 0
        [void][FfxiWindowProbe]::GetWindowThreadProcessId($handle, [ref]$ownerId)
        $process = $processes | Where-Object Id -EQ $ownerId | Select-Object -First 1
        if ($null -eq $process -or -not [FfxiWindowProbe]::IsWindowVisible($handle)) { return $true }
        $rect = [FfxiWindowProbe+Rect]::new()
        $point = [FfxiWindowProbe+Point]::new()
        if (-not [FfxiWindowProbe]::GetClientRect($handle, [ref]$rect) -or -not [FfxiWindowProbe]::ClientToScreen($handle, [ref]$point)) { return $true }
        $windows.Add([pscustomobject]@{
            ProcessId = $ownerId; Process = $process.ProcessName; Handle = $handle.ToInt64()
            ClientLeft = $point.X; ClientTop = $point.Y; ClientWidth = $rect.Right; ClientHeight = $rect.Bottom
            Dpi = [FfxiWindowProbe]::GetDpiForWindow($handle)
            Foreground = ($handle -eq [FfxiWindowProbe]::GetForegroundWindow())
        })
        return $true
    }
    [void][FfxiWindowProbe]::EnumWindows($callback, [IntPtr]::Zero)
    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
    ConvertTo-Json -InputObject @($windows.ToArray()) -Depth 4 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $OutputDirectory "$stamp-windows.json")
    $windows.ToArray() | Format-Table
    if ($Capture) {
        if ($ProcessId -eq 0) { throw 'Capture requires an explicit -ProcessId. No screenshot saved.' }
        $target = @($windows | Where-Object { $_.Foreground -and $_.ClientWidth -gt 0 -and $_.ClientHeight -gt 0 })
        if ($target.Count -ne 1) { throw 'Expected one foreground window for the selected process. No screenshot saved.' }
        Add-Type -AssemblyName System.Drawing
        $bitmap = [System.Drawing.Bitmap]::new($target[0].ClientWidth, $target[0].ClientHeight)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CopyFromScreen($target[0].ClientLeft, $target[0].ClientTop, 0, 0, $bitmap.Size)
            $bitmap.Save((Join-Path $OutputDirectory "$stamp-client.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
} finally { if ($previousDpi -ne [IntPtr]::Zero) { [void][FfxiWindowProbe]::SetThreadDpiAwarenessContext($previousDpi) } }
