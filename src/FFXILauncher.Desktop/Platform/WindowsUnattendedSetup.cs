using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace FFXILauncher.Desktop.Platform;

public static class WindowsUnattendedSetup
{
    public static bool IsElevated
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
    public static async Task InstallAsync()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var identity = WindowsIdentity.GetCurrent();
        var script = Path.Combine(AppContext.BaseDirectory, "tools", "install-unattended.ps1");
        if (!File.Exists(script)) throw new InvalidOperationException("Extract the complete Windows release before installing unattended access.");
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = true, Verb = IsElevated ? "" : "runas", WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-ExpectedSid", identity.User!.Value }) info.ArgumentList.Add(argument);
        try
        {
            using var setup = Process.Start(info) ?? throw new InvalidOperationException("Windows could not start unattended setup.");
            await setup.WaitForExitAsync();
            if (setup.ExitCode != 0) throw new InvalidOperationException("Unattended setup failed. Close the installed manager before updating it, and approve using the same Windows account.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { throw new InvalidOperationException("Setup was cancelled. One Windows administrator approval is required to register unattended access."); }
    }
}
