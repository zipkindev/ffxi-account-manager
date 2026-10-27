namespace FFXILauncher.Core;

public enum DisconnectFollowup { Wait, ResumeCharacterSelection, FreshRestart }
public static class DisconnectRecovery
{
    public static DisconnectFollowup Followup(string? firstCode, string? secondCode, LoginCheckpoint screen) =>
        firstCode == "POL-1039" || secondCode == "POL-1039" ? DisconnectFollowup.Wait :
        firstCode != null && firstCode == secondCode ? DisconnectFollowup.FreshRestart :
        firstCode == null && secondCode == null && screen is LoginCheckpoint.Agreement or LoginCheckpoint.Title or LoginCheckpoint.Characters or LoginCheckpoint.CharacterConfirmation
            ? DisconnectFollowup.ResumeCharacterSelection : DisconnectFollowup.Wait;
    public static bool IsDisconnectCode(string? code) => code is "POL-0250" or "FFXI-4001" or "FFXI-4002" or "FFXI-3100" or "POL-0512";
    public static bool CanRestart(AccountConfiguration account, bool intentionallyStopped, bool credentialRejected, string? confirmedCode) =>
        account.MaintainConnection && account.AutoLoginEnabled && !intentionallyStopped && !credentialRejected && IsDisconnectCode(confirmedCode);
    public static bool CanCloseHung(AccountConfiguration account, bool intentionallyStopped, SessionHealth health,
        DateTimeOffset? unresponsiveSince, DateTimeOffset now, bool liveHeartbeat, bool recentServerTraffic) =>
        account.MaintainConnection && account.AutoLoginEnabled && !intentionallyStopped && health == SessionHealth.Unresponsive &&
        unresponsiveSince is {} since && now - since >= TimeSpan.FromSeconds(90) && !liveHeartbeat && !recentServerTraffic;
}

// Separate from the rendering heartbeat: a disconnect dialog can keep rendering.
public sealed record ServerTrafficSample(DateTimeOffset ReportedUtc, DateTimeOffset LastIncomingUtc)
{
    // Quiet sessions can legitimately report chunks about a minute apart.
    public bool IsRecent(DateTimeOffset now) => ReportedUtc <= now.AddSeconds(3) &&
        now - ReportedUtc <= TimeSpan.FromSeconds(45) && ReportedUtc - LastIncomingUtc <= TimeSpan.FromSeconds(90);
}

// Share the report read between the dashboard and recovery watchdog. A changed
// process or character session must never inherit another session's evidence.
public sealed class ServerTrafficCache
{
    private sealed record Entry(string Directory, ManagedProcess Process, string Nonce, DateTimeOffset RegisteredUtc,
        string Character, string World, DateTimeOffset CheckedUtc, ServerTrafficSample? Sample);
    private readonly Dictionary<Guid, Entry> entries = [];
    public ServerTrafficSample? Read(string directory, AccountConfiguration account, ManagedProcess process, GameSession? session, DateTimeOffset now)
    {
        if (session == null || session.RegisteredUtc < process.StartedUtc.AddSeconds(-2) ||
            !session.Character.Equals(account.Character, StringComparison.OrdinalIgnoreCase) ||
            !session.World.Equals(account.World, StringComparison.OrdinalIgnoreCase)) return null;
        if (entries.TryGetValue(account.Id, out var old) && old.Directory == directory && old.Process == process &&
            old.Nonce == session.Nonce && old.RegisteredUtc == session.RegisteredUtc &&
            old.Character == account.Character && old.World == account.World &&
            now >= old.CheckedUtc && now - old.CheckedUtc < TimeSpan.FromSeconds(30)) return old.Sample;
        var sample = ServerTraffic.Read(directory, account, session, now);
        entries[account.Id] = new(directory, process, session.Nonce, session.RegisteredUtc, account.Character, account.World, now, sample);
        return sample;
    }
}

// Missing files during the addon's replace operation must not steal focus.
// Any healthy sample resets suspicion; process replacement starts a new wait.
public sealed class ConnectionInspectionGate
{
    private sealed record Observation(DateTimeOffset CheckedUtc, DateTimeOffset? ReportedUtc, int Misses);
    private readonly Dictionary<ManagedProcess, Observation> observations = [];
    private readonly Dictionary<ManagedProcess, DateTimeOffset> nextRead = [];
    public bool ShouldRead(ManagedProcess process, DateTimeOffset now)
    {
        if (nextRead.TryGetValue(process, out var next) && now < next && next - now <= TimeSpan.FromSeconds(30)) return false;
        nextRead[process] = now.AddSeconds(30);
        return true;
    }
    public bool Observe(ManagedProcess process, ServerTrafficSample? traffic, DateTimeOffset now)
    {
        var known = observations.TryGetValue(process, out var previous);
        if (traffic?.IsRecent(now) == true && (!known || previous!.ReportedUtc == null || traffic.ReportedUtc > previous.ReportedUtc))
        { observations[process] = new(now, traffic.ReportedUtc, 0); return false; }
        if (!known || now < previous!.CheckedUtc)
        { observations[process] = new(now, traffic?.ReportedUtc, 0); return false; }
        if (now - previous.CheckedUtc < TimeSpan.FromSeconds(30)) return false;
        var misses = Math.Min(3, previous.Misses + 1);
        observations[process] = new(now, traffic?.ReportedUtc ?? previous.ReportedUtc, misses);
        return misses >= 3;
    }
    public void Forget(ManagedProcess process) { observations.Remove(process); nextRead.Remove(process); }
}

public static class ServerTraffic
{
    public static bool IsRecent(string directory, AccountConfiguration account, GameSession? session, DateTimeOffset now) =>
        Read(directory, account, session, now)?.IsRecent(now) == true;
    public static ServerTrafficSample? Read(string directory, AccountConfiguration account, GameSession? session, DateTimeOffset now)
    {
        if (session == null) return null;
        try
        {
            var path = Path.Combine(directory, ShutdownIpc.Stem(account) + ".connection");
            if (!File.Exists(path) || new FileInfo(path).Length > 4096) return null;
            var lines = File.ReadAllLines(path);
            if (lines.Length is not (6 or 10) || lines[0] != session.Nonce ||
                !lines[1].Equals(account.Character, StringComparison.OrdinalIgnoreCase) || !lines[2].Equals(account.World, StringComparison.OrdinalIgnoreCase) ||
                !long.TryParse(lines[3], out var registered) || !long.TryParse(lines[4], out var reported) || !long.TryParse(lines[5], out var incoming)) return null;
            var reportTime = DateTimeOffset.FromUnixTimeSeconds(reported);
            var incomingTime = DateTimeOffset.FromUnixTimeSeconds(incoming);
            return registered == session.RegisteredUtc.ToUnixTimeSeconds() && incoming >= registered && incoming <= reported &&
                reportTime <= now.AddSeconds(3) && now - reportTime <= TimeSpan.FromSeconds(45)
                    ? new(reportTime, incomingTime) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }
}

// Only the explicit version-communication Retry control uses this policy.
public sealed class NetworkRetryBackoff
{
    public int Attempts { get; private set; }
    public TimeSpan ReserveRetry()
    {
        if (Attempts >= 3) throw new InvalidOperationException("POL cannot reach the version service after three retries. The window remains open; recovery will check again later.");
        return TimeSpan.FromSeconds(30 * (1 << Attempts++));
    }
}
