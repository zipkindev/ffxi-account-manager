using System.Text.Json;

namespace FFXILauncher.Core;

public sealed record ManagedSession(Guid AccountId, ManagedProcess Process, string PolPath)
{
    public bool Matches(ManagedProcess live, string executablePath) => Process == live && PolPath.Equals(executablePath, StringComparison.OrdinalIgnoreCase);
}

// Ownership hints survive UI restarts; the platform must verify the exact live
// process and executable before restoring them. This file contains no secrets.
public sealed class ManagedSessionStore(string directory)
{
    private readonly string path = Path.Combine(directory, "managed-sessions.json");
    public IReadOnlyList<ManagedSession> Load()
    {
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Managed session file is too large.");
        var sessions = JsonSerializer.Deserialize<ManagedSession[]>(File.ReadAllText(path)) ?? throw new InvalidDataException("Managed session file is empty.");
        Validate(sessions);
        return sessions;
    }
    public void Save(IEnumerable<ManagedSession> values)
    {
        var sessions = values.ToArray();
        Validate(sessions);
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(sessions));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void Validate(ManagedSession[] sessions)
    {
        if (sessions.Length > 256 || sessions.Any(s => s == null || s.AccountId == Guid.Empty || s.Process == null || s.Process.Pid <= 0 || s.Process.StartedUtc.Kind != DateTimeKind.Utc || string.IsNullOrWhiteSpace(s.PolPath)) ||
            sessions.Select(s => s.AccountId).Distinct().Count() != sessions.Length || sessions.Select(s => s.Process.Pid).Distinct().Count() != sessions.Length)
            throw new InvalidDataException("Managed session mappings contain invalid or duplicate ownership.");
    }
}
