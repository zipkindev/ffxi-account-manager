namespace FFXILauncher.Core;

public enum DisconnectFollowup { Wait, ResumeCharacterSelection, FreshRestart }
public static class DisconnectRecovery
{
    public static DisconnectFollowup Followup(string? firstCode, string? secondCode, LoginCheckpoint screen) =>
        firstCode != null && firstCode == secondCode ? DisconnectFollowup.FreshRestart :
        firstCode == null && secondCode == null && screen is LoginCheckpoint.Agreement or LoginCheckpoint.Title or LoginCheckpoint.Characters or LoginCheckpoint.CharacterConfirmation
            ? DisconnectFollowup.ResumeCharacterSelection : DisconnectFollowup.Wait;
    public static bool IsDisconnectCode(string? code) => code is "POL-0250" or "FFXI-4001" or "FFXI-4002";
    public static bool CanRestart(AccountConfiguration account, bool intentionallyStopped, bool credentialRejected, string? confirmedCode) =>
        account.MaintainConnection && account.AutoLoginEnabled && !intentionallyStopped && !credentialRejected && IsDisconnectCode(confirmedCode);
    public static bool CanCloseHung(AccountConfiguration account, bool intentionallyStopped, SessionHealth health,
        DateTimeOffset? unresponsiveSince, DateTimeOffset now, bool liveHeartbeat, bool recentServerTraffic) =>
        account.MaintainConnection && account.AutoLoginEnabled && !intentionallyStopped && health == SessionHealth.Unresponsive &&
        unresponsiveSince is {} since && now - since >= TimeSpan.FromSeconds(90) && !liveHeartbeat && !recentServerTraffic;
}

// Separate from the rendering heartbeat: a disconnect dialog can keep rendering.
public static class ServerTraffic
{
    public static bool IsRecent(string directory, AccountConfiguration account, GameSession? session, DateTimeOffset now)
    {
        if (session == null) return false;
        try
        {
            var path = Path.Combine(directory, ShutdownIpc.Stem(account) + ".connection");
            if (!File.Exists(path) || new FileInfo(path).Length > 4096) return false;
            var lines = File.ReadAllLines(path);
            if (lines.Length != 6 || lines[0] != session.Nonce ||
                !lines[1].Equals(account.Character, StringComparison.OrdinalIgnoreCase) || !lines[2].Equals(account.World, StringComparison.OrdinalIgnoreCase) ||
                !long.TryParse(lines[3], out var registered) || !long.TryParse(lines[4], out var reported) || !long.TryParse(lines[5], out var incoming)) return false;
            var reportTime = DateTimeOffset.FromUnixTimeSeconds(reported);
            var incomingTime = DateTimeOffset.FromUnixTimeSeconds(incoming);
            return registered == session.RegisteredUtc.ToUnixTimeSeconds() && incoming >= registered && incoming <= reported &&
                reportTime <= now.AddSeconds(3) && now - reportTime <= TimeSpan.FromSeconds(10) && now - incomingTime <= TimeSpan.FromSeconds(30);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
}
