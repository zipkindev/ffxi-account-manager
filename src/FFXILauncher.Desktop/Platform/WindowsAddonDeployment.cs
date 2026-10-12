using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace FFXILauncher.Desktop.Platform;

public static class WindowsAddonDeployment
{
    public const string HelperArgument = "--deploy-addon";
    public static int RunHelper(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args.Length != 3 || args[0] != HelperArgument) return 2;
        using var identity = WindowsIdentity.GetCurrent();
        // Keep the IPC token bound to the desktop user. A different administrator's
        // credentials would otherwise make the data inaccessible to the manager.
        if (identity.User?.Value != args[2]) return 3;
        try
        {
            WindowerAddonInstaller.Install(args[1], AppPayload.AddonDirectory);
            return 0;
        }
        catch (UnauthorizedAccessException) { return 5; }
        catch { return 1; }
    }
    public static async Task<AddonDeployment> InstallAsync(string windowerPath, Action requestingPermission)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Addon deployment requires Windows.");
        var payload = AppPayload.AddonDirectory;
        try { return WindowerAddonInstaller.Install(windowerPath, payload); }
        catch (UnauthorizedAccessException)
        {
            requestingPermission();
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value ?? throw new InvalidOperationException("Windows user identity is unavailable.");
            var executable = AppPayload.Executable;
            if (!File.Exists(executable)) throw new FileNotFoundException("The Windows deployment helper is missing. Use the complete Windows package.");
            var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory };
            start.ArgumentList.Add(HelperArgument);
            start.ArgumentList.Add(Path.GetFullPath(windowerPath));
            start.ArgumentList.Add(sid);
            Process process;
            try { process = Process.Start(start) ?? throw new IOException("Could not start the addon installer."); }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { throw new InvalidOperationException("Windows permission prompt was canceled. Click Deploy again to authorize installation."); }
            using (process)
            {
                await process.WaitForExitAsync();
                if (process.ExitCode == 3) throw new InvalidOperationException("Deployment requires elevation for this Windows user. A different administrator account cannot own this user's private IPC data.");
                if (process.ExitCode != 0) throw new IOException("Administrator deployment failed (code " + process.ExitCode + "). Check Windower folder permissions and security software, then retry.");
            }
            var root = Path.GetDirectoryName(Path.GetFullPath(windowerPath))!;
            var destination = Path.Combine(root, "addons", "FFXIManager");
            var init = Path.Combine(root, "scripts", "init.txt");
            // Verify the result as the normal desktop user, including ongoing IPC writes.
            var probe = Path.Combine(destination, "data", Guid.NewGuid().ToString("N") + ".permission-check");
            try { File.WriteAllText(probe, ""); }
            finally { if (File.Exists(probe)) File.Delete(probe); }
            if (!File.Exists(Path.Combine(destination, "FFXIManager.lua")) || WindowerStartupScriptMissing(init))
                throw new IOException("Deployment could not be verified. Check the Windower folder and retry.");
            return new(destination, init, true, null);
        }
    }
    private static bool WindowerStartupScriptMissing(string init) => !File.Exists(init) || FFXILauncher.Core.WindowerStartupScript.EnsureAutoLoad(File.ReadAllBytes(init)).Changed;
}
