using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace FFXILauncher.Core;

public sealed record MaintenanceWindow(string Source, string Title, DateTimeOffset StartUtc, DateTimeOffset EndUtc, string? World = null);
public sealed record MaintenanceSnapshot(List<MaintenanceWindow> Windows, DateTimeOffset? CheckedUtc, List<string>? ReviewRequired = null);
public sealed record RecoveryDecision(bool Allowed, string Reason, DateTimeOffset? ResumeUtc = null);
public static class MaintenanceSchedule
{
    public static bool Applies(MaintenanceWindow window, string world) => window.World == null || window.World.Equals(world, StringComparison.OrdinalIgnoreCase);
    public static IReadOnlyList<MaintenanceWindow> UpcomingForCharacter(IEnumerable<MaintenanceWindow> windows, string? world, DateTimeOffset now) =>
        windows.Where(w => w.EndUtc > now && (w.World == null || (!string.IsNullOrWhiteSpace(world) && w.World.Equals(world, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(w => w.StartUtc).ThenBy(w => w.EndUtc).ToArray();
    public static MaintenanceWindow? CardAlert(IEnumerable<MaintenanceWindow> windows, string? world, DateTimeOffset now)
    {
        var upcoming = UpcomingForCharacter(windows, world, now).Where(w => w.StartUtc <= now.AddHours(12)).ToArray();
        return upcoming.FirstOrDefault(w => w.World != null) ?? upcoming.FirstOrDefault(w => w.World == null);
    }
    public static MaintenanceWindow? ShutdownDue(AccountConfiguration account, MaintenanceSnapshot snapshot, DateTimeOffset now)
    {
        if (!account.ScheduleMaintenance || !account.MaintainConnection || string.IsNullOrWhiteSpace(account.World) ||
            snapshot.CheckedUtc == null || now - snapshot.CheckedUtc > TimeSpan.FromMinutes(15) || snapshot.ReviewRequired?.Count > 0) return null;
        return snapshot.Windows.Where(w => Applies(w, account.World) && now >= w.StartUtc.AddMinutes(-5) && now < w.EndUtc)
            .OrderBy(w => w.StartUtc).FirstOrDefault();
    }
}

public static class RecoveryPolicy
{
    public static RecoveryDecision Evaluate(AccountConfiguration account, bool intentionallyStopped, MaintenanceSnapshot snapshot, DateTimeOffset now)
    {
        var decision = Evaluate(account, intentionallyStopped, snapshot.Windows, now);
        if (!decision.Allowed) return decision;
        if (snapshot.ReviewRequired?.Count > 0) return new(false, "Official maintenance notice needs time-format review");
        if (snapshot.CheckedUtc == null || now - snapshot.CheckedUtc > TimeSpan.FromMinutes(15)) return new(false, "Waiting for a fresh official maintenance check");
        return decision;
    }
    public static RecoveryDecision Evaluate(AccountConfiguration account, bool intentionallyStopped, IEnumerable<MaintenanceWindow> windows, DateTimeOffset now)
    {
        if (intentionallyStopped) return new(false, "Intentionally stopped");
        if (!account.MaintainConnection) return new(false, "Maintain connection disabled");
        if (string.IsNullOrWhiteSpace(account.World)) return new(false, "Character world needs verification");
        var active = windows.Where(w => MaintenanceSchedule.Applies(w, account.World) &&
            now >= (account.ScheduleMaintenance ? w.StartUtc.AddMinutes(-5) : w.StartUtc) && now < w.EndUtc.AddMinutes(2)).OrderByDescending(w => w.EndUtc).FirstOrDefault();
        return active == null ? new(true, "Ready for reconnect") : new(false, "Waiting for server maintenance", active.EndUtc.AddMinutes(2));
    }
}

public static class MaintenanceNoticeParser
{
    private static readonly Regex Range = new(@"(?<start>[A-Z][a-z]{2}\.\s+\d{1,2},\s+\d{4}\s+(?:from\s+)?\d{1,2}:\d{2})\s+to\s+(?<end>(?:[A-Z][a-z]{2}\.\s+\d{1,2},\s+\d{4}\s+)?\d{1,2}:\d{2})\s*\((?<zone>PDT|PST|GMT|UTC)\)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    public static string PlainText(string html) => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*>", " ", RegexOptions.None, TimeSpan.FromSeconds(1))), @"\s+", " ", RegexOptions.None, TimeSpan.FromSeconds(1));
    public static MaintenanceWindow? Parse(string source, string title, string html, string? world = null)
    {
        var uri = new Uri(source);
        if (uri.Scheme != "https" || uri.Host != "www.playonline.com") throw new ArgumentException("Expected the official maintenance source.");
        // Website/forum/transfer-only notices must not stop game reconnection.
        if (!title.Contains("All Worlds", StringComparison.OrdinalIgnoreCase) && !title.Contains("PlayOnline", StringComparison.OrdinalIgnoreCase) && world == null) return null;
        var text = PlainText(html);
        var marker = text.IndexOf("[Date & Time]", StringComparison.OrdinalIgnoreCase);
        if (marker < 0 || !text.Contains("FINAL FANTASY XI", StringComparison.OrdinalIgnoreCase)) return null;
        var ranges = Range.Matches(text[(marker + 13)..]);
        // Separate date ranges need a verified world-to-range mapping, never assume the first applies to every world.
        if (ranges.Count != 1) return null;
        var match = ranges[0];
        var offset = match.Groups["zone"].Value switch { "PDT" => TimeSpan.FromHours(-7), "PST" => TimeSpan.FromHours(-8), _ => TimeSpan.Zero };
        if (!DateTime.TryParseExact(Regex.Replace(match.Groups["start"].Value, @"\bfrom\s+", "", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)), "MMM'.' d, yyyy H:mm", CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var start)) return null;
        var endText = match.Groups["end"].Value;
        DateTime end;
        if (endText.Contains(','))
        {
            if (!DateTime.TryParseExact(endText, "MMM'.' d, yyyy H:mm", CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out end)) return null;
        }
        else
        {
            if (!TimeOnly.TryParseExact(endText, "H:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) return null;
            end = start.Date.Add(time.ToTimeSpan());
            if (end <= start) end = end.AddDays(1);
        }
        var startUtc = new DateTimeOffset(DateTime.SpecifyKind(start, DateTimeKind.Unspecified), offset).ToUniversalTime();
        var endUtc = new DateTimeOffset(DateTime.SpecifyKind(end, DateTimeKind.Unspecified), offset).ToUniversalTime();
        if (endUtc <= startUtc || endUtc - startUtc > TimeSpan.FromDays(3)) return null;
        return new(source, title, startUtc, endUtc, world);
    }
}
