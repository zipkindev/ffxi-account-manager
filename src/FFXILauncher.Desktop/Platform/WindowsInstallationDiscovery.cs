using System.Diagnostics;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace FFXILauncher.Desktop.Platform;

public sealed record InstallationDiscovery(string? WindowerPath, string? PolPath, IReadOnlyList<string> Profiles, string Status);
public static class WindowsInstallationDiscovery
{
    public static InstallationDiscovery Detect()
    {
        if (!OperatingSystem.IsWindows()) return new(null, null, [], "Installation detection is available on Windows.");
        var windower = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pol = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(HashSet<string> paths, string? path, string executable)
        {
            if (!string.IsNullOrWhiteSpace(path) && Path.GetFileName(path).Equals(executable, StringComparison.OrdinalIgnoreCase) && File.Exists(path)) paths.Add(Path.GetFullPath(path));
        }
        foreach (var name in new[] { "Windower", "pol" })
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                try
                {
                    Add(name == "pol" ? pol : windower, WindowsSessionPlatform.ExecutablePath(process.Id), name == "pol" ? "pol.exe" : "Windower.exe");
                    if (name != "pol") continue;
                    foreach (ProcessModule module in process.Modules)
                    {
                        var parent = Path.GetDirectoryName(module.FileName);
                        for (var depth = 0; depth < 3 && parent != null; depth++, parent = Path.GetDirectoryName(parent))
                            Add(windower, Path.Combine(parent, "Windower.exe"), "Windower.exe");
                    }
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException) { }
            }
        }
        // Prefer paths found in active sessions. Installation paths are fallback candidates.
        if (windower.Count == 0)
        {
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Path.GetPathRoot(Environment.SystemDirectory)! })
            foreach (var folder in new[] { "Windower", "Windower4" }) Add(windower, Path.Combine(root, folder, "Windower.exe"), "Windower.exe");
        }
        if (pol.Count == 0)
        {
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            foreach (var region in new[] { "PlayOnlineUS", "PlayOnlineEU", "PlayOnline" })
            {
                try
                {
                    using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using var key = machine.OpenSubKey("SOFTWARE\\" + region + "\\InstallFolder");
                    if (key == null) continue;
                    foreach (var value in key.GetValueNames())
                        if (key.GetValue(value) is string folder) Add(pol, Path.Combine(folder, "pol.exe"), "pol.exe");
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
            }
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
                Add(pol, Path.Combine(root, "PlayOnline", "SquareEnix", "PlayOnlineViewer", "pol.exe"), "pol.exe");
        }
        var windowerPath = windower.Count == 1 ? windower.Single() : null;
        var polPath = pol.Count == 1 ? pol.Single() : null;
        return new(windowerPath, polPath, ReadProfiles(windowerPath),
            (windowerPath != null ? "Windower detected." : windower.Count > 1 ? "Multiple Windower installations found; choose a path." : "Windower not detected; enter a path.") + " " +
            (polPath != null ? "POL detected." : pol.Count > 1 ? "Multiple POL installations found; choose a path." : "POL not detected; enter a path."));
    }
    public static string? ResolveProfile(string? windowerPath, string? configured)
    {
        var profiles = ReadProfileNames(windowerPath);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var exact = profiles.FirstOrDefault(p => p.Equals(configured, StringComparison.OrdinalIgnoreCase));
            return exact ?? (configured.Equals("Default", StringComparison.OrdinalIgnoreCase) && profiles.Contains("") ? "" : null);
        }
        if (profiles.Contains("")) return ""; // Windower's unnamed default profile.
        return profiles.Count == 1 ? profiles[0] : profiles.FirstOrDefault(p => p.Equals("Default", StringComparison.OrdinalIgnoreCase));
    }
    public static IReadOnlyList<string> ReadProfiles(string? windowerPath) => ReadProfileNames(windowerPath)
        .Select(p => p.Length == 0 ? "Default" : p).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static IReadOnlyList<string> ReadProfileNames(string? windowerPath)
    {
        if (string.IsNullOrWhiteSpace(windowerPath)) return [];
        try
        {
            var path = Path.Combine(Path.GetDirectoryName(windowerPath)!, "settings.xml");
            if (!File.Exists(path)) return [];
            using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1_000_000 });
            return XDocument.Load(reader).Descendants("profile").Select(p => p.Attribute("name")?.Value).Where(p => p != null).Select(p => p!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException or ArgumentException) { return []; }
    }
}
