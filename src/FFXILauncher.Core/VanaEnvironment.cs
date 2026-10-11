namespace FFXILauncher.Core;

public sealed record VanaClockReading(int TimeMinutes, string Day, string MoonPhase, int MoonPercent)
{
    public string DayElement => Day switch
    {
        "Firesday" => "Fire", "Earthsday" => "Earth", "Watersday" => "Water",
        "Windsday" => "Wind", "Iceday" => "Ice", "Lightningday" => "Lightning",
        "Lightsday" => "Light", "Darksday" => "Dark",
        _ => throw new InvalidOperationException("Unknown Vana’diel day.")
    };
    public string Describe() => $"Vana’diel · {TimeMinutes / 60:00}:{TimeMinutes % 60:00} {Day} · Moon: {MoonPhase} ({MoonPercent}%)";
}

public static class VanaClock
{
    // The standard Vana'diel calendar: UTC Unix seconds plus 92514960,
    // accelerated 25 times. One day is 3456 real seconds; moon cycle 84 days.
    // Reference: BalladOfWorms/OmniWatch, clock formula documentation.
    private static readonly string[] Days = ["Firesday", "Earthsday", "Watersday", "Windsday", "Iceday", "Lightningday", "Lightsday", "Darksday"];
    public static VanaClockReading Read(DateTimeOffset utc)
    {
        var seconds = (utc.ToUnixTimeMilliseconds() / 1000d + 92514960) * 25;
        var days = (long)Math.Floor(seconds / 86400);
        var minute = (int)Math.Floor((seconds - days * 86400) / 60);
        var cycle = (int)((days + 26) % 84 + 84) % 84;
        var percent = (int)Math.Round(Math.Abs(42 - cycle) * 100d / 42, MidpointRounding.AwayFromZero);
        var waning = cycle < 42;
        var phase = percent >= 93 ? "Full Moon" : percent <= 5 ? "New Moon" : percent is >= 43 and <= 57 ? (waning ? "Last Quarter" : "First Quarter") : percent > 57 ? (waning ? "Waning Gibbous" : "Waxing Gibbous") : (waning ? "Waning Crescent" : "Waxing Crescent");
        return new(minute, Days[(int)((days % 8 + 8) % 8)], phase, percent);
    }
}

public sealed record WeatherEffect(string? Element, int Intensity)
{
    public string Describe(string weather) => Element is {} element
        ? $"{weather} · {element} ×{Intensity} ({(Intensity == 2 ? "double" : "single")})"
        : $"{weather} · {(weather is "Fine patches" or "Sunshine" or "Clouds" or "Fog" ? "no elemental weather" : "element unknown")}";

    // Element/intensity values from Windower's weather resources. Do not infer
    // "double weather" from words like storm; Dust storms is single Earth.
    public static WeatherEffect FromName(string weather) => weather switch
    {
        "Hot spells" => new("Fire", 1), "Heat waves" => new("Fire", 2),
        "Snow" => new("Ice", 1), "Blizzards" => new("Ice", 2),
        "Winds" => new("Wind", 1), "Gales" => new("Wind", 2),
        "Dust storms" => new("Earth", 1), "Sand storms" => new("Earth", 2),
        "Thunder" => new("Lightning", 1), "Thunderstorms" => new("Lightning", 2),
        "Rain" => new("Water", 1), "Squalls" => new("Water", 2),
        "Auroras" => new("Light", 1), "Stellar glare" => new("Light", 2),
        "Gloom" => new("Dark", 1), "Darkness" => new("Dark", 2),
        _ => new(null, 0)
    };
}
