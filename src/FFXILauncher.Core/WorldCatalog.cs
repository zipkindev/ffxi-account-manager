namespace FFXILauncher.Core;

public static class WorldCatalog
{
    // Retail worlds listed by https://www.playonline.com/pcd/service/ff11usindex.html
    public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(new[]
    {
        "Asura", "Bahamut", "Bismarck", "Carbuncle", "Cerberus", "Fenrir", "Lakshmi", "Leviathan",
        "Odin", "Phoenix", "Quetzalcoatl", "Ragnarok", "Shiva", "Siren", "Sylph", "Valefor"
    });
    public static string? Normalize(string? value) => Names.FirstOrDefault(w => w.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase));
}
