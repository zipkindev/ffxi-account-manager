using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace FFXILauncher.Desktop;

public sealed record AppTheme(string Id, string Name, string Background, string Surface, string Inset, string Text, string Muted, string Accent, bool Light = false, string? Artwork = null, bool Portrait = false, string? Credit = null, double SideFocus = .5, double BackgroundFocus = .5)
{
    public override string ToString() => Name;
}

public static class ThemeCatalog
{
    public static IReadOnlyList<AppTheme> All { get; } = new AppTheme[]
    {
        new("midnight", "UI · Midnight", "#101720", "#192330", "#121B26", "#E5EDF5", "#A8B7C9", "#55D9BD"),
        new("nord", "UI · Nord", "#2E3440", "#3B4252", "#303744", "#ECEFF4", "#C0CBDA", "#88C0D0"),
        new("dracula", "UI · Dracula", "#282A36", "#343746", "#222430", "#F8F8F2", "#C4BDDF", "#BD93F9"),
        new("mocha", "UI · Mocha", "#1E1E2E", "#313244", "#181825", "#CDD6F4", "#BAC2DE", "#CBA6F7"),
        new("adoulin", "FFXI · Western Adoulin", "#151B22", "#E51E2933", "#EB141D26", "#F2E9DC", "#CEC7BB", "#E9BB72", Artwork: "Adoulin.jpg"),
        new("rakaznar", "FFXI · Promyvion", "#191626", "#E52C2541", "#EB201B32", "#F2EAFD", "#D1C0E1", "#C0A0F0", Artwork: "Promyvion.jpg"),
        new("eastern-adoulin", "FFXI · Eastern Adoulin", "#101820", "#E51B2937", "#F0111C27", "#E7EDF2", "#C3CFD8", "#97C9D6", Artwork: "EasternAdoulin.jpg", Portrait: false),
        new("ulbuka", "FFXI · East Ulbuka", "#101820", "#E51B2937", "#F0111C27", "#E7EDF2", "#C3CFD8", "#98CAB5", Artwork: "Ulbuka.jpg", Portrait: false),
        new("amano-aht", "FFXI · Treasures of Aht Urhgan", "#101820", "#E51B2937", "#F0111C27", "#E7EDF2", "#C3CFD8", "#D5C59D", Artwork: "Amano5.jpg", Portrait: true, Credit: "©2006 YOSHITAKA AMANO · © SQUARE ENIX"),
        new("maw", "FFXI · Cavernous Maw", "#101820", "#E51B2937", "#F0111C27", "#E7EDF2", "#C3CFD8", "#BBAFD0", Artwork: "Maw.jpg", Portrait: true),
        new("amano-blue", "FFXI · Amano · Blue illustration", "#101820", "#E51B2937", "#F0111C27", "#E7EDF2", "#C3CFD8", "#95D8E2", Artwork: "Amano13.jpg", Portrait: false, Credit: "Artwork: YOSHITAKA AMANO · © SQUARE ENIX"),
        new("amano-map", "FFXI · Amano · World map", "#101820", "#E51B2937", "#F0111C27", "#E7EDF2", "#C3CFD8", "#DEBE76", Artwork: "Amano0.jpg", Portrait: false, Credit: "Artwork: YOSHITAKA AMANO · © SQUARE ENIX"),
        new("amano-zilart", "FFXI · Amano · Zilart", "#101820", "#E51B2937", "#F0111C27", "#E7EDF2", "#C3CFD8", "#A1CDE8", Artwork: "Amano1.jpg", Portrait: false, Credit: "Artwork: YOSHITAKA AMANO · © SQUARE ENIX"),
        new("arita-crystal", "FFXI · Arita · Crystal illustration", "#101820", "#E51B2937", "#F0111C27", "#E7EDF2", "#C3CFD8", "#9FD9D4", Artwork: "Arita0.jpg", Portrait: false),
        new("ffxi-original", "FFXI · Original FFXI", "#101820", "#E51B2937", "#F0111C27", "#E7EDF2", "#C3CFD8", "#8CD4E3", Artwork: "Illustration0.jpg", Portrait: true),
        new("abyssea", "FFXI · Abyssea", "#101820", "#E51B2937", "#F0111C27", "#E7EDF2", "#C3CFD8", "#BDA5D9", Artwork: "Illustration6.jpg", Portrait: true)
    };

    public static IReadOnlyList<string> Landscapes { get; } = new[] { "Ulbuka.jpg", "Adoulin.jpg", "EasternAdoulin.jpg", "Promyvion.jpg" };
    public static string DefaultLandscape(Guid account) => Landscapes[(int)(BitConverter.ToUInt32(account.ToByteArray(), 0) % (uint)Landscapes.Count)];
    public static AppTheme Resolve(string? id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];
    public static void Apply(AppTheme theme)
    {
        var app = Application.Current!;
        app.RequestedThemeVariant = theme.Light ? ThemeVariant.Light : ThemeVariant.Dark;
        foreach (var (key, value) in new[] { ("Background", theme.Background), ("Surface", theme.Surface), ("Inset", theme.Inset), ("Text", theme.Text), ("Muted", theme.Muted), ("Accent", theme.Accent) })
            app.Resources["Theme" + key + "Brush"] = Brush.Parse(value);
        app.Resources["ThemeAccentTextBrush"] = Brush.Parse(theme.Light ? "#FFFFFF" : "#102720");
        app.Resources["ThemeDangerBrush"] = Brush.Parse(theme.Light ? "#A12233" : "#FF8C8C");
        app.Resources["ThemeWarningBrush"] = Brush.Parse(theme.Light ? "#874000" : "#FFD180");
        app.Resources["SystemAccentColor"] = Color.Parse(theme.Accent);
    }
    public static IBrush BrushFor(string key) => (IBrush)Application.Current!.Resources["Theme" + key + "Brush"]!;
}
