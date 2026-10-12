using System.Text.RegularExpressions;

namespace FFXILauncher.Core;

public sealed record CharacterState(string Nonce, DateTimeOffset ReportedUtc, int Hp, int MaxHp,
    int Mp, int MaxMp, bool IsDead, int ZoneId, string Zone, DateTimeOffset? LoggedOutUtc, ZoneEnvironment? Environment = null, CharacterJobs? Jobs = null)
{
    public bool DeathConfirmed => IsDead && Hp == 0 && ZoneId != 280 &&
        !Zone.Equals("Mog House", StringComparison.OrdinalIgnoreCase) && !Zone.Equals("Mog Garden", StringComparison.OrdinalIgnoreCase);
    public string Describe(bool live, bool includeEnvironment = true)
    {
        var condition = DeathConfirmed ? (LoggedOutUtc != null ? "Dead at logout" : "Dead") : "Alive";
        var prefix = live ? "Current" : LoggedOutUtc != null ? "Last logout" : "Last reported";
        var vitals = Hp == 0 && !DeathConfirmed ? "HP/MP pending" : $"HP {Hp}/{MaxHp} · MP {Mp}/{MaxMp}";
        var text = $"{prefix} · {Zone} · {condition} · {vitals}";
        if (live && includeEnvironment) text += Environment is {} environment ? "\n" + environment.Describe() : "\nWeather / Vana’diel time / moon pending · reload FFXIManager";
        if (!live) text += $"\nStats reported {ReportedUtc.ToLocalTime():g}";
        if (LoggedOutUtc is { } logout) text += $" · Logged out {logout.ToLocalTime():g}";
        if (Jobs != null) text = (live ? "" : "Last job · ") + Jobs.Describe() + "\n" + text;
        return text;
    }
}

public sealed record CharacterJobs(int? MasterLevel, string MainJob, int MainLevel, string SubJob, int SubLevel)
{
    internal static readonly HashSet<string> JobNames = new("WAR MNK WHM BLM RDM THF PLD DRK BST BRD RNG SAM NIN DRG SMN BLU COR PUP DNC SCH GEO RUN NON".Split(' '));
    public string Describe() => (MainLevel == 99 ? $"ML {(MasterLevel is {} master ? master.ToString() : "?")} " : "") +
        $"{MainLevel}{MainJob}" + (SubJob == "NON" ? "" : $"/{SubLevel}{SubJob}");
}

public sealed record ZoneEnvironment(string Weather, int TimeMinutes, string Day, string MoonPhase, int MoonPercent)
{
    public string Describe() => $"Weather: {Weather} · Vana’diel: {TimeMinutes / 60:00}:{TimeMinutes % 60:00} {Day} · Moon: {MoonPhase} ({MoonPercent}%)";
}

public sealed class CharacterStateReader(string dataDirectory)
{
    public CharacterState? Read(AccountConfiguration account, DateTimeOffset now)
    {
        var path = Path.Combine(dataDirectory, ShutdownIpc.Stem(account) + ".state");
        string[] lines;
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 4096) return null;
            lines = File.ReadAllLines(path);
        }
        catch (FileNotFoundException) { return null; }
        if (lines.Length != 13 || !Regex.IsMatch(lines[0], "^[0-9a-f_]{16,96}$") ||
            !lines[1].Equals(account.Character, StringComparison.OrdinalIgnoreCase) ||
            !lines[2].Equals(account.World, StringComparison.OrdinalIgnoreCase) ||
            !long.TryParse(lines[3], out var registered) || !long.TryParse(lines[4], out var reported) ||
            !int.TryParse(lines[5], out var hp) || hp < 0 ||
            !int.TryParse(lines[6], out var maxHp) || maxHp <= 0 || hp > maxHp ||
            !int.TryParse(lines[7], out var mp) || mp < 0 ||
            !int.TryParse(lines[8], out var maxMp) || maxMp < 0 || mp > maxMp ||
            lines[9] is not ("0" or "1") || !int.TryParse(lines[10], out var zoneId) || zoneId <= 0 ||
            string.IsNullOrWhiteSpace(lines[11]) || lines[11].Length > 256 || lines[11].Any(char.IsControl) ||
            !long.TryParse(lines[12], out var loggedOut) || loggedOut < 0) return null;
        try
        {
            var start = DateTimeOffset.FromUnixTimeSeconds(registered);
            var report = DateTimeOffset.FromUnixTimeSeconds(reported);
            DateTimeOffset? logout = loggedOut == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(loggedOut);
            if (start > report || report > now.AddSeconds(3) || logout < report || logout > now.AddSeconds(3)) return null;
            return new(lines[0], report, hp, maxHp, mp, maxMp, lines[9] == "1", zoneId, lines[11], logout,
                ReadEnvironment(Path.ChangeExtension(path, ".environment"), lines, zoneId),
                ReadJobs(Path.ChangeExtension(path, ".jobs"), lines));
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }
    private static CharacterJobs? ReadJobs(string path, string[] state)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 4096) return null;
            var lines = File.ReadAllLines(path);
            if (lines.Length != 10 || !lines.Take(5).SequenceEqual(state.Take(5)) ||
                !int.TryParse(lines[5], out var master) || master is < -1 or > 50 ||
                !CharacterJobs.JobNames.Contains(lines[6]) || lines[6] == "NON" ||
                !int.TryParse(lines[7], out var mainLevel) || mainLevel is < 1 or > 99 ||
                !CharacterJobs.JobNames.Contains(lines[8]) ||
                !int.TryParse(lines[9], out var subLevel) || subLevel is < 0 or > 99 ||
                (lines[8] == "NON") != (subLevel == 0)) return null;
            return new(master < 0 ? null : master, lines[6], mainLevel, lines[8], subLevel);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
    private static ZoneEnvironment? ReadEnvironment(string path, string[] state, int zoneId)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 4096) return null;
            var lines = File.ReadAllLines(path);
            if (lines.Length != 11 || !lines.Take(5).SequenceEqual(state.Take(5)) ||
                !int.TryParse(lines[5], out var environmentZone) || environmentZone != zoneId ||
                !int.TryParse(lines[7], out var time) || time < 0 || time >= 1440 ||
                !int.TryParse(lines[10], out var moon) || moon < 0 || moon > 100 ||
                new[] { lines[6], lines[8], lines[9] }.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 128 || s.Any(char.IsControl))) return null;
            return new(lines[6], time, lines[8], lines[9], moon);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
