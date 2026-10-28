using System.Text.Json;

namespace FFXILauncher.Core;

public sealed class FriendListRecoveryException(string message) : InvalidOperationException(message);

public static class FriendListRecovery
{
    public const int MaximumRetries = 3;
    public const string LimitMessage = "POL-0008: automatic Retry limit reached. Click Retry manually or restart this account; POL remains open.";
    public static bool IsDialog(LoginFrame frame) => LoginCheckpoints.NetworkDialogCode(frame) == "POL-0008";
    public static bool IsCredentialScreen(LoginCheckpoint checkpoint) => checkpoint is LoginCheckpoint.MemberList or LoginCheckpoint.MemberInformation or LoginCheckpoint.Connect or LoginCheckpoint.PasswordEditor;
    private static OcrLine? Button(LoginFrame frame)
    {
        var line = GameUpdateWorkflow.Control(frame, "Retry");
        return line != null && line.Words.All(w => w.X >= frame.Width * .5 && w.X + w.Width <= frame.Width * .78 &&
            w.Y >= frame.Height * .75 && w.Y + w.Height <= frame.Height * .9) ? line : null;
    }
    public static bool Matches(LoginFrame first, LoginFrame second)
    {
        if (!IsDialog(first) || !IsDialog(second) || first.Width != second.Width || first.Height != second.Height ||
            Button(first) is not {} a || Button(second) is not {} b) return false;
        return Math.Abs(a.Words.Min(w => w.X) - b.Words.Min(w => w.X)) <= 2 &&
            Math.Abs(a.Words.Min(w => w.Y) - b.Words.Min(w => w.Y)) <= 2;
    }
    public static async Task<TimeSpan> RetryAsync(LoginFrame first, Func<CancellationToken, Task<LoginFrame>> read,
        Func<TimeSpan, CancellationToken, Task> wait, Func<LoginFrame, CancellationToken, Task> click,
        Func<TimeSpan> reserve, Action<TimeSpan> report, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        await wait(TimeSpan.FromMilliseconds(500), cancellation);
        var confirmed = await read(cancellation);
        if (!Matches(first, confirmed)) throw new FriendListRecoveryException("POL-0008: the friend-list dialog or Retry button could not be confirmed. No input was sent.");
        var delay = reserve(); // Persist the reservation before waiting or input.
        report(delay);
        await wait(delay, cancellation);
        var afterWait = await read(cancellation);
        await wait(TimeSpan.FromMilliseconds(500), cancellation);
        var checkedFrame = await read(cancellation);
        if (!Matches(confirmed, afterWait) || !Matches(afterWait, checkedFrame))
            throw new FriendListRecoveryException("POL-0008: the dialog changed during the wait. No Retry click was sent.");
        cancellation.ThrowIfCancellationRequested();
        await click(checkedFrame, cancellation);
        return delay;
    }
}

// A budget is tied to one account and exact process creation time. Worker/UI
// restarts cannot renew it. No credentials or recognized screen text are stored.
public sealed class FriendListRetryStore(string root)
{
    private sealed record Budget(ManagedProcess Process, int Attempts);
    private string PathFor(Guid account) => account != Guid.Empty ? Path.Combine(root, "friend-list-retries", account.ToString("N") + ".json") : throw new ArgumentException("An account is required.");
    public int Attempts(Guid account, ManagedProcess process)
    {
        var path = PathFor(account);
        if (!File.Exists(path)) return 0;
        if (new FileInfo(path).Length > 4096) throw new InvalidDataException("POL-0008: retry history is invalid; POL remains open.");
        var budget = JsonSerializer.Deserialize<Budget>(File.ReadAllText(path));
        if (budget == null || budget.Process == null || budget.Process.Pid <= 0 || budget.Process.StartedUtc.Kind != DateTimeKind.Utc || budget.Attempts is < 0 or > FriendListRecovery.MaximumRetries)
            throw new InvalidDataException("POL-0008: retry history is invalid; POL remains open.");
        return budget.Process == process ? budget.Attempts : 0;
    }
    public TimeSpan ReserveRetry(Guid account, ManagedProcess process)
    {
        if (process.Pid <= 0 || process.StartedUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("An exact process identity is required.");
        using var lease = Lock(account);
        var attempts = Attempts(account, process);
        if (attempts >= FriendListRecovery.MaximumRetries) throw new FriendListRecoveryException(FriendListRecovery.LimitMessage);
        var path = PathFor(account);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(new Budget(process, attempts + 1))); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return TimeSpan.FromSeconds(30 * (1 << attempts));
    }
    private FileStream Lock(Guid account)
    {
        var path = PathFor(account);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    public void Clear(Guid account, ManagedProcess process)
    {
        if (!File.Exists(PathFor(account))) return;
        using var lease = Lock(account);
        if (Attempts(account, process) > 0) File.Delete(PathFor(account));
    }
}
