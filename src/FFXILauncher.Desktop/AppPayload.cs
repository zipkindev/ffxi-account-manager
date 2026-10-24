using System.Reflection;
using System.Security.Cryptography;
using FFXILauncher.Desktop.Platform;

namespace FFXILauncher.Desktop;

// Single-file builds materialize only signed-in user's bundled support files.
// Assemblies and native dependencies remain the runtime's responsibility.
internal static class AppPayload
{
    private const string Prefix = "FFXILauncher.Payload.";
    private static readonly Assembly Assembly = typeof(AppPayload).Assembly;
    public static string BuildIdentity => Assembly.ManifestModule.ModuleVersionId.ToString("N");
    public static bool Standalone => Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Any(a => a.Key == "Standalone" && string.Equals(a.Value, "true", StringComparison.OrdinalIgnoreCase));
    public static string Executable => Environment.ProcessPath ?? throw new IOException("App executable path is unavailable.");
    private static readonly Lazy<string> Root = new(Materialize);

    public static string FilePath(string relative) => Path.Combine(Standalone ? Root.Value : AppContext.BaseDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
    public static string AddonDirectory => Path.GetDirectoryName(FilePath("windower_addons/FFXIManager/FFXIManager.lua"))!;

    public static string InstallSource()
    {
        if (!Standalone) return AppContext.BaseDirectory;
        var root = Root.Value;
        var destination = Path.Combine(root, "FFXILauncher.Desktop.exe");
        if (System.IO.File.Exists(destination) && (System.IO.File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Installation payload cannot be a link.");
        if (!System.IO.File.Exists(destination) || !Hash(destination).AsSpan().SequenceEqual(Hash(Executable)))
        {
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".next";
            System.IO.File.Copy(Executable, temporary);
            System.IO.File.Move(temporary, destination, true);
        }
        return root;
    }
    private static byte[] Hash(string path)
    {
        using var file = System.IO.File.OpenRead(path);
        return SHA256.HashData(file);
    }
    private static string Materialize()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FFXILauncher", "app-payload", BuildIdentity);
        Create(root);
        foreach (var resource in Assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            var relative = resource[Prefix.Length..];
            var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid bundled payload path.");
            Create(Path.GetDirectoryName(path)!);
            if (System.IO.File.Exists(path) && (System.IO.File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Support file cannot be a link.");
            using var stream = Assembly.GetManifestResourceStream(resource)!;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            if (System.IO.File.Exists(path) && System.IO.File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) continue;
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".next";
            System.IO.File.WriteAllBytes(temporary, bytes);
            System.IO.File.Move(temporary, path, true);
        }
        return root;
    }
    private static void Create(string directory)
    {
        if (Directory.Exists(directory) && (System.IO.File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("Support directory cannot be a link.");
        if (OperatingSystem.IsWindows()) WindowsPrivateDirectory.Create(directory);
        else Directory.CreateDirectory(directory);
    }
    public static object VerifyPackage()
    {
        var count = 0;
        foreach (var resource in Assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            var path = FilePath(resource[Prefix.Length..]);
            using var stream = Assembly.GetManifestResourceStream(resource)!;
            using var file = System.IO.File.OpenRead(path);
            if (!SHA256.HashData(stream).AsSpan().SequenceEqual(SHA256.HashData(file))) throw new IOException("Bundled support file verification failed.");
            count++;
        }
        using var iconStream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://FFXILauncher.Desktop/Assets/Weather/Water.png"));
        using var icon = new Avalonia.Media.Imaging.Bitmap(iconStream);
        var nativeImageDecoded = icon.PixelSize.Width > 0 && icon.PixelSize.Height > 0;
        using var applicationIconStream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://FFXILauncher.Desktop/Assets/AppIcon.ico"));
        var applicationIcon = new Avalonia.Controls.WindowIcon(applicationIconStream);
        using var encodedIcon = new MemoryStream();
        applicationIcon.Save(encodedIcon);
        if (encodedIcon.Length == 0) throw new IOException("Invalid bundled application icon.");
        var artworkThemesVerified = 0;
        foreach (var theme in ThemeCatalog.All.Where(t => t.Artwork != null))
        {
            using var artworkStream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://FFXILauncher.Desktop/Assets/Themes/" + theme.Artwork));
            using var artwork = new Avalonia.Media.Imaging.Bitmap(artworkStream);
            if (artwork.PixelSize.Width <= 0 || artwork.PixelSize.Height <= 0) throw new IOException("Invalid bundled artwork.");
            if (theme.SideArtwork != null)
            {
                using var sideStream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://FFXILauncher.Desktop/Assets/Themes/" + theme.SideArtwork));
                using var side = new Avalonia.Media.Imaging.Bitmap(sideStream);
                if (side.PixelSize.Width <= 0 || side.PixelSize.Height <= 0) throw new IOException("Invalid bundled side artwork.");
            }
            artworkThemesVerified++;
        }
        var installSource = InstallSource();
        return new { Version = AppRelease.Version, Standalone, SupportFilesVerified = count, ArtworkThemesVerified = artworkThemesVerified, NativeImageDecoded = nativeImageDecoded, ApplicationIconDecoded = true,
            InstallerPrepared = System.IO.File.Exists(Path.Combine(installSource, "FFXILauncher.Desktop.exe")),
            LicensesAvailable = AppRelease.Licenses.Contains("MIT License"), BuildIdentity, LoginFlowFingerprint = Platform.LoginFlowCompatibility.Current };
    }
}
