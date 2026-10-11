using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FFXILauncher.Core;

namespace FFXILauncher.Desktop.Platform;

public sealed record AddonDeployment(string AddonDirectory, string InitPath, bool StartupAdded, string? BackupPath);
public static class WindowerAddonInstaller
{
    public static AddonDeployment Install(string windowerPath, string payloadDirectory)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Addon deployment is available on Windows.");
        if (!File.Exists(windowerPath) || !Path.GetFileName(windowerPath).Equals("Windower.exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Choose a valid Windower.exe first.");
        if (!File.Exists(Path.Combine(payloadDirectory, "FFXIManager.lua"))) throw new FileNotFoundException("The bundled FFXIManager addon was not found.");
        var root = Path.GetDirectoryName(Path.GetFullPath(windowerPath))!;
        var destination = Path.Combine(root, "addons", "FFXIManager");
        var data = Path.Combine(destination, "data");
        var token = Path.Combine(data, "control.token");
        if (File.Exists(token))
        {
            if (new FileInfo(token).Length > 128 || !Regex.IsMatch(File.ReadAllText(token).Trim(), "^[0-9a-f]{64}$")) throw new InvalidDataException("Existing addon control token is invalid. It was preserved; repair it before deployment.");
        }
        var scripts = Path.Combine(root, "scripts");
        var init = Path.Combine(scripts, "init.txt");
        var originalExists = File.Exists(init);
        if (originalExists && new FileInfo(init).Length > 1_000_000) throw new InvalidDataException("init.txt is too large to update automatically.");
        var original = originalExists ? File.ReadAllBytes(init) : [];
        var update = WindowerStartupScript.EnsureAutoLoad(original);
        WindowsPrivateDirectory.Create(data);
        if (!File.Exists(token)) AtomicWrite(token, Encoding.ASCII.GetBytes(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()), overwrite: false);
        foreach (var file in Directory.EnumerateFiles(payloadDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(payloadDirectory, file);
            // Keep per-user tokens, requests and telemetry when updating the addon.
            if (relative.Split(Path.DirectorySeparatorChar)[0].Equals("data", StringComparison.OrdinalIgnoreCase)) continue;
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            AtomicWrite(target, File.ReadAllBytes(file), overwrite: true);
        }
        string? backup = null;
        if (update.Changed)
        {
            Directory.CreateDirectory(scripts);
            if (originalExists)
            {
                // Refuse to overwrite a script another editor changed during deployment.
                if (!File.ReadAllBytes(init).AsSpan().SequenceEqual(original)) throw new IOException("init.txt changed during deployment; rerun installation after saving your edits.");
                backup = init + ".ffximanager-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".bak";
                File.Copy(init, backup, false);
                AtomicWrite(init, update.Content, overwrite: true);
            }
            else AtomicWrite(init, update.Content, overwrite: false);
        }
        return new(destination, init, update.Changed, backup);
    }
    private static void AtomicWrite(string path, byte[] content, bool overwrite)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".next";
        try { File.WriteAllBytes(temporary, content); File.Move(temporary, path, overwrite); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
