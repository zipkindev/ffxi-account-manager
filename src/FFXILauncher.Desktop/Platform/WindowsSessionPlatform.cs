using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using FFXILauncher.Core;

namespace FFXILauncher.Desktop.Platform;

public sealed class LaunchUncertainException(string message, Exception? inner = null) : Exception(message, inner);

public sealed record RunningGameProcess(ManagedProcess Process, string ExecutablePath, string? Character);

public sealed class WindowsSessionPlatform : ISessionPlatform
{
    private readonly SemaphoreSlim launchGate = new(1);
    public bool CanLaunch => OperatingSystem.IsWindows();
    public async Task<ManagedProcess> LaunchAsync(LauncherConfiguration configuration, AccountConfiguration account, IReadOnlyCollection<ManagedProcess> knownSessions, CancellationToken cancellationToken)
    {
        if (!CanLaunch) throw new PlatformNotSupportedException("Game launching is currently supported on Windows.");
        if (!WindowsUnattendedSetup.IsElevated) throw new InvalidOperationException("Start the manager from its unattended desktop shortcut to launch Windower without UAC prompts.");
        if (!File.Exists(configuration.WindowerPath) || !File.Exists(configuration.PolPath))
            throw new InvalidOperationException("Set valid Windower.exe and pol.exe paths first.");
        if (!Path.GetFileName(configuration.WindowerPath).Equals("Windower.exe", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(configuration.PolPath).Equals("pol.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Select Windower.exe and pol.exe.");
        var profile = WindowsInstallationDiscovery.ResolveProfile(configuration.WindowerPath, account.Profile) ?? throw new InvalidOperationException("Choose a Windower profile; no Default or unique profile was detected.");
        await launchGate.WaitAsync(cancellationToken);
        var dispatched = false;
        try
        {
            var running = FindPol(configuration.PolPath);
            if (running.Any(p => !knownSessions.Contains(p)))
                throw new InvalidOperationException("Untracked POL processes are already running. Close them before launching from this preview.");
            var before = running.Select(p => p.Pid).ToHashSet();
            var started = DateTime.UtcNow;
            var info = new ProcessStartInfo(configuration.WindowerPath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(configuration.WindowerPath)!
            };
            info.ArgumentList.Add("--profile");
            info.ArgumentList.Add(profile);
            using var launcher = Process.Start(info) ?? throw new InvalidOperationException("Windower could not start.");
            dispatched = true;
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while (DateTime.UtcNow < deadline)
            {
                // Cancellation after dispatch must not lose ownership of a process already launched.
                var candidates = FindPol(configuration.PolPath).Where(p => !before.Contains(p.Pid) && p.StartedUtc >= started.AddSeconds(-1)).ToArray();
                if (candidates.Length > 1)
                    throw new InvalidOperationException("Multiple new POL processes found. Inspect sessions before launching again.");
                if (candidates.Length == 1) return candidates[0];
                await Task.Delay(250);
            }
            throw new InvalidOperationException("No unique POL process identified. Check Windower before retrying; a session may already exist.");
        }
        catch (Exception ex) when (dispatched) { throw new LaunchUncertainException(ex.Message, ex); }
        finally { launchGate.Release(); }
    }
    private static List<ManagedProcess> FindPol(string path)
    {
        var result = new List<ManagedProcess>();
        foreach (var process in Process.GetProcessesByName("pol"))
        {
            using (process)
            {
                try
                {
                    if (string.Equals(ExecutablePath(process.Id), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                        result.Add(new(process.Id, process.StartTime.ToUniversalTime()));
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    if (process.HasExited) continue;
                    throw new InvalidOperationException("Cannot inspect a running POL process. Close it or run the manager at the same privilege level.", ex);
                }
            }
        }
        return result;
    }
    public IReadOnlyList<RunningGameProcess> Discover()
    {
        if (!CanLaunch) return [];
        var results = new List<RunningGameProcess>();
        foreach (var process in Process.GetProcessesByName("pol"))
        {
            using (process)
            {
                try
                {
                    var title = process.MainWindowTitle.Trim();
                    results.Add(new(new(process.Id, process.StartTime.ToUniversalTime()), ExecutablePath(process.Id),
                        Regex.IsMatch(title, "^[A-Za-z]+$") && !title.Equals("PlayOnline", StringComparison.OrdinalIgnoreCase) ? title : null));
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            }
        }
        return results;
    }
    internal static string ExecutablePath(int pid)
    {
        var handle = OpenProcess(0x1000, false, pid);
        if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var buffer = new StringBuilder(32768);
            var size = buffer.Capacity;
            if (!QueryFullProcessImageName(handle, 0, buffer, ref size)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return buffer.ToString();
        }
        finally { CloseHandle(handle); }
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    public SessionHealth Inspect(ManagedProcess process)
    {
        try
        {
            using var current = Process.GetProcessById(process.Pid);
            if (current.HasExited || current.StartTime.ToUniversalTime() != process.StartedUtc) return SessionHealth.Exited;
            if (current.MainWindowHandle == IntPtr.Zero) return SessionHealth.AwaitingLogin;
            return current.Responding ? SessionHealth.Responsive : SessionHealth.Unresponsive;
        }
        catch (ArgumentException) { return SessionHealth.Exited; }
        catch (InvalidOperationException) { return SessionHealth.Exited; }
        catch (System.ComponentModel.Win32Exception) { return SessionHealth.Unknown; }
    }
}
