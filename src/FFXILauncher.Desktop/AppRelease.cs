namespace FFXILauncher.Desktop;

internal static class AppRelease
{
    public static string Version => typeof(AppRelease).Assembly.GetName().Version is {} version
        ? $"{version.Major}.{version.Minor}.{version.Build}" : "unknown";

    public static string Notes
    {
        get
        {
            using var stream = typeof(AppRelease).Assembly.GetManifestResourceStream("FFXILauncher.ReleaseNotes")
                ?? throw new InvalidOperationException("Bundled release notes are missing.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
