using System.Text.RegularExpressions;

namespace FFXILauncher.Core;
public sealed record GameSession(string Nonce, string Character, string World, DateTimeOffset RegisteredUtc, DateTimeOffset HeartbeatUtc);
public sealed class ShutdownIpc(string dataDirectory)
{
    public static bool HasAdvanced(GameSession first, GameSession current) =>
        first.Nonce == current.Nonce && first.RegisteredUtc == current.RegisteredUtc &&
        first.Character.Equals(current.Character, StringComparison.OrdinalIgnoreCase) &&
        first.World.Equals(current.World, StringComparison.OrdinalIgnoreCase) &&
        current.HeartbeatUtc > first.HeartbeatUtc;

    internal static string Stem(AccountConfiguration account)
    {
        if (!Regex.IsMatch(account.Character, "^[A-Za-z]+$") || !Regex.IsMatch(account.World, "^[A-Za-z]+$"))
            throw new InvalidOperationException("Character and world must contain letters only.");
        return account.World.ToLowerInvariant() + "_" + account.Character.ToLowerInvariant();
    }
    public GameSession? ReadSession(AccountConfiguration account, DateTimeOffset now)
    {
        var path = Path.Combine(dataDirectory, Stem(account) + ".status");
        if (!File.Exists(path)) return null;
        string[] lines;
        try { if (new FileInfo(path).Length > 4096) return null; lines = File.ReadAllLines(path); }
        catch (IOException) { return null; }
        if (lines.Length != 5 || !Regex.IsMatch(lines[0], "^[0-9a-f_]{16,96}$") ||
            !lines[1].Equals(account.Character, StringComparison.OrdinalIgnoreCase) || !lines[2].Equals(account.World, StringComparison.OrdinalIgnoreCase) ||
            !long.TryParse(lines[3], out var registered) || !long.TryParse(lines[4], out var heartbeat)) return null;
        DateTimeOffset start, last;
        try { start = DateTimeOffset.FromUnixTimeSeconds(registered); last = DateTimeOffset.FromUnixTimeSeconds(heartbeat); }
        catch (ArgumentOutOfRangeException) { return null; }
        if (last > now.AddSeconds(3) || now - last > TimeSpan.FromSeconds(10) || start > last) return null;
        return new(lines[0], lines[1], lines[2], start, last);
    }
    public GameSession? DiscoverCharacter(string character, DateTimeOffset processStartedUtc, DateTimeOffset now)
    {
        if (!Regex.IsMatch(character, "^[A-Za-z]+$") || !Directory.Exists(dataDirectory)) return null;
        var matches = new List<GameSession>();
        foreach (var path in Directory.EnumerateFiles(dataDirectory, "*_" + character.ToLowerInvariant() + ".status"))
        {
            var world = Path.GetFileNameWithoutExtension(path).Split('_')[0];
            if (!Regex.IsMatch(world, "^[A-Za-z]+$")) continue;
            var account = new AccountConfiguration(Guid.Empty, "", "", character, world, "", "");
            var session = ReadSession(account, now);
            if (session != null && session.RegisteredUtc >= processStartedUtc.AddSeconds(-2)) matches.Add(session);
        }
        return matches.Count == 1 ? matches[0] : null;
    }
    public Task RequestAsync(AccountConfiguration account, GameSession expected, CancellationToken cancellationToken) => SendAsync(account, expected, "shutdown", cancellationToken);
    public Task CancelAsync(AccountConfiguration account, GameSession expected, CancellationToken cancellationToken) => SendAsync(account, expected, "cancel_shutdown", cancellationToken);
    private async Task SendAsync(AccountConfiguration account, GameSession expected, string operation, CancellationToken cancellationToken)
    {
        var current = ReadSession(account, DateTimeOffset.UtcNow);
        if (current == null || current.Nonce != expected.Nonce) throw new InvalidOperationException("Game session is stale or changed; no shutdown sent.");
        var token = File.ReadAllText(Path.Combine(dataDirectory, "control.token")).Trim();
        if (!Regex.IsMatch(token, "^[0-9a-f]{64}$")) throw new InvalidDataException("Invalid control token.");
        var stem = Stem(account);
        var path = Path.Combine(dataDirectory, stem + ".request");
        var nonce = Guid.NewGuid().ToString("N");
        var temporary = path + "." + nonce + ".next";
        try
        {
            await File.WriteAllTextAsync(temporary, $"{token}\n{nonce}\n{expected.Nonce}\n{operation}\n{DateTimeOffset.UtcNow.AddSeconds(15).ToUnixTimeSeconds()}\n", cancellationToken);
            File.Move(temporary, path, false);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(18);
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var receipt = Path.Combine(dataDirectory, stem + ".receipt");
                if (File.Exists(receipt))
                {
                    var lines = File.ReadAllLines(receipt);
                    if (lines.Length == 3 && lines[0] == nonce && lines[1] == expected.Nonce)
                    {
                        if (lines[2] == "dispatched") return;
                        if (lines[2] is "error" or "rejected") throw new InvalidOperationException("The addon rejected the shutdown request.");
                    }
                }
                await Task.Delay(200, cancellationToken);
            }
            throw new TimeoutException("Shutdown acknowledgement timed out. The request may have been dispatched; inspect the game before retrying.");
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            // Remove only our unconsumed request. Never remove another writer's message.
            if (File.Exists(path))
            {
                var lines = File.ReadAllLines(path);
                if (lines.Length >= 2 && lines[1] == nonce) File.Delete(path);
            }
        }
    }
}
