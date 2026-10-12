namespace FFXILauncher.Desktop;

internal static class AppRelease
{
    public static string Version => typeof(AppRelease).Assembly.GetName().Version is {} version
        ? $"{version.Major}.{version.Minor}.{version.Build}" : "unknown";

    public static string Notes => Read("ReleaseNotes");
    public static string Licenses => Read("License") + "\n\n" + Read("ArtworkNotice") + "\n\n" + Read("ThirdPartyNotices");

    private static string Read(string name)
    {
        var resource = name switch { "License" => "Payload.LICENSE", "ArtworkNotice" => "Payload.ARTWORK-NOTICE.md", "ThirdPartyNotices" => "Payload.THIRD-PARTY-NOTICES.md", _ => name };
        using var stream = typeof(AppRelease).Assembly.GetManifestResourceStream("FFXILauncher." + resource)
            ?? throw new InvalidOperationException("Bundled document is missing: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
