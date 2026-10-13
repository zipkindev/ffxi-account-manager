using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;

namespace FFXILauncher.Core;

public sealed record InGameMaintenanceAlert(string Session, string Character, string World, DateTimeOffset RegisteredUtc, DateTimeOffset ObservedUtc, string Message);
public static class InGameMaintenance
{
    public static InGameMaintenanceAlert? Parse(string[] lines, DateTimeOffset now)
    {
        if (lines.Length != 7 || !Regex.IsMatch(lines[0], "^[0-9a-f_]{16,96}$") ||
            !Regex.IsMatch(lines[1], "^[A-Za-z]+$") || !WorldCatalog.Names.Contains(lines[2], StringComparer.OrdinalIgnoreCase) ||
            lines[5] != "incoming-017-system" || lines[6].Length is < 1 or > 512 || lines[6].Any(char.IsControl) ||
            !lines[6].Contains("maintenance", StringComparison.OrdinalIgnoreCase) ||
            !long.TryParse(lines[3], out var registered) || !long.TryParse(lines[4], out var observed)) return null;
        try
        {
            var start = DateTimeOffset.FromUnixTimeSeconds(registered);
            var time = DateTimeOffset.FromUnixTimeSeconds(observed);
            return start > time || time > now.AddSeconds(3) ? null : new(lines[0], lines[1], lines[2], start, time, lines[6]);
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }
    private static string AlertKey(InGameMaintenanceAlert alert) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{alert.Session}|{alert.ObservedUtc.ToUnixTimeSeconds()}|{alert.Message}")));
    public static void Acknowledge(string directory, InGameMaintenanceAlert alert)
    {
        // Record exactly the observed warning; a concurrent/new warning gets a different key.
        File.WriteAllText(Path.Combine(directory, alert.World.ToLowerInvariant() + "_" + alert.Character.ToLowerInvariant() + ".alert.ack"), AlertKey(alert));
    }
    public static IReadOnlyList<InGameMaintenanceAlert> Read(string directory, string world, DateTimeOffset now)
    {
        if (!Directory.Exists(directory) || !WorldCatalog.Names.Contains(world, StringComparer.OrdinalIgnoreCase)) return [];
        var alerts = new List<InGameMaintenanceAlert>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.alert"))
        {
            try
            {
                if (new FileInfo(path).Length > 4096) continue;
                var alert = Parse(File.ReadAllLines(path), now);
                if (alert != null && alert.World.Equals(world, StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(path).Equals(alert.World.ToLowerInvariant() + "_" + alert.Character.ToLowerInvariant() + ".alert", StringComparison.OrdinalIgnoreCase))
                {
                    var ack = path + ".ack";
                    if (File.Exists(ack) && new FileInfo(ack).Length <= 128 && File.ReadAllText(ack) == AlertKey(alert)) continue;
                    alerts.Add(alert);
                }
            }
            catch (IOException) { }
        }
        return alerts;
    }
    private static MaintenanceWindow[] Matching(InGameMaintenanceAlert alert, MaintenanceSnapshot snapshot) =>
        snapshot.Windows.Where(w => MaintenanceSchedule.Applies(w, alert.World) &&
            w.EndUtc >= alert.ObservedUtc && w.StartUtc <= alert.ObservedUtc.AddHours(2)).ToArray();
    private static bool Fresh(MaintenanceSnapshot snapshot, DateTimeOffset now) =>
        snapshot.CheckedUtc != null && now - snapshot.CheckedUtc <= TimeSpan.FromMinutes(15) && snapshot.ReviewRequired?.Count is not > 0;
    public static bool Completed(InGameMaintenanceAlert alert, MaintenanceSnapshot snapshot, DateTimeOffset now)
    {
        var matching = Matching(alert, snapshot);
        return Fresh(snapshot, now) && matching.Length > 0 && now >= matching.Max(w => w.EndUtc);
    }
    public static RecoveryDecision? Hold(IEnumerable<InGameMaintenanceAlert> alerts, MaintenanceSnapshot snapshot, DateTimeOffset now)
    {
        foreach (var alert in alerts.OrderByDescending(a => a.ObservedUtc))
        {
            // A previously completed window cannot explain a newly received warning.
            var matching = Matching(alert, snapshot);
            if (matching.Length == 0) return new(false, "In-game maintenance warning · end time needs review");
            if (!Fresh(snapshot, now))
                return new(false, "In-game maintenance warning · waiting for confirmed schedule");
            // Announcements can precede scheduled maintenance by hours.
            // They must not widen an upcoming official window into an active hold.
            if (now < matching.Min(w => w.StartUtc)) continue;
            var active = matching.Where(w => now >= w.StartUtc && now < w.EndUtc).OrderByDescending(w => w.EndUtc).FirstOrDefault();
            if (active != null) return new(false, "In-game maintenance warning · reconnect paused", active.EndUtc);
        }
        return null;
    }
}
