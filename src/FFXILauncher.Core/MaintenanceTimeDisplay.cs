using System.Globalization;

namespace FFXILauncher.Core;

public static class MaintenanceTimeDisplay
{
    public static string PacificRange(DateTimeOffset start, DateTimeOffset end) =>
        Range(start, end, TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Pacific Standard Time" : "America/Los_Angeles"));

    private static string ZoneLabel(DateTimeOffset local, TimeZoneInfo zone) =>
        $"{(zone.IsDaylightSavingTime(local) ? zone.DaylightName : zone.StandardName)} (UTC{local:zzz})";

    public static string Instant(DateTimeOffset instant, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        return $"{local.ToString("ddd, MMM d, yyyy h:mm tt", CultureInfo.CurrentCulture)} · {ZoneLabel(local, zone)}";
    }

    public static string Range(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var first = TimeZoneInfo.ConvertTime(start, zone);
        var last = TimeZoneInfo.ConvertTime(end, zone);
        if (ZoneLabel(first, zone) != ZoneLabel(last, zone))
            return $"{Instant(start, zone)} – {Instant(end, zone)}";
        var endFormat = first.Date == last.Date ? "h:mm tt" : "MMM d, yyyy h:mm tt";
        return $"{first.ToString("ddd, MMM d, yyyy h:mm tt", CultureInfo.CurrentCulture)} – {last.ToString(endFormat, CultureInfo.CurrentCulture)} · {ZoneLabel(first, zone)}";
    }
}
