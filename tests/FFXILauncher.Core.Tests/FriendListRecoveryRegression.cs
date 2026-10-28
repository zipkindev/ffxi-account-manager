using System.Text.Json;
using FFXILauncher.Core;

internal static class FriendListRecoveryRegression
{
    internal static void Run(Action<bool, string> check)
    {
        var dialog = JsonSerializer.Deserialize<LoginFrame>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "friend-network-0008.json")))!;
        check(FriendListRecovery.IsDialog(dialog) && FriendListRecovery.Matches(dialog, dialog), "captured POL-0008 friend-list dialog has exact code, wording and bounded Retry");
        var wrongHeading = dialog with { Lines = dialog.Lines.Where(l => !l.Text.Contains("Friend List")).ToArray() };
        check(!FriendListRecovery.IsDialog(wrongHeading), "POL-0008 without friend-list heading cannot authorize Retry");
        var footer = dialog with { Lines = dialog.Lines.Select(l => l with { Words = l.Words.Select(w => w with { Y = dialog.Height * .95 }).ToArray() }).ToArray() };
        check(!FriendListRecovery.IsDialog(footer), "footer-only POL-0008 cannot authorize Retry");
        var conflicting = dialog with { Lines = dialog.Lines.Append(new OcrLine("POL-1039", [new("POL-1039", 100, 180, 90, 20)])).ToArray() };
        check(!FriendListRecovery.IsDialog(conflicting), "conflicting bounded error codes reject friend-list Retry");
        var wrongCode = dialog with { Lines = dialog.Lines.Select(l => l with { Text = l.Text.Replace("POL-0008", "POL-0003") }).ToArray() };
        check(!FriendListRecovery.IsDialog(wrongCode), "POL-0003 cannot use the POL-0008 Retry path");
        var retry = dialog.UniqueLine("Retry")!;
        var duplicate = dialog with { Lines = dialog.Lines.Append(retry).ToArray() };
        check(!FriendListRecovery.Matches(dialog, duplicate), "duplicate Retry controls cannot authorize input");
        var moved = dialog with { Lines = dialog.Lines.Select(l => l == retry ? l with { Words = l.Words.Select(w => w with { X = w.X + 20 }).ToArray() } : l).ToArray() };
        check(!FriendListRecovery.Matches(dialog, moved), "moved Retry geometry during backoff rejects input");
        check(!FriendListRecovery.Matches(dialog, dialog with { Width = 1280 }), "changed window dimensions reject stale input");
        check(FriendListRecovery.IsCredentialScreen(LoginCheckpoint.Connect) && FriendListRecovery.IsCredentialScreen(LoginCheckpoint.PasswordEditor) && !FriendListRecovery.IsCredentialScreen(LoginCheckpoint.Home), "friend-list Retry may resume authenticated menus but stops at credential screens");
        var root = Path.Combine(Path.GetTempPath(), "ffxi-friend-retry-" + Guid.NewGuid().ToString("N"));
        var account = Guid.NewGuid(); var other = Guid.NewGuid();
        var process = new ManagedProcess(1234, DateTime.UtcNow);
        try
        {
            var clicks = 0;
            var waits = new List<TimeSpan>();
            for (var attempt = 0; attempt < 3; attempt++)
            {
                // A new store models a new helper/UI instance on each pass.
                var store = new FriendListRetryStore(root);
                var delay = FriendListRecovery.RetryAsync(dialog, _ => Task.FromResult(dialog), (time, _) => { waits.Add(time); return Task.CompletedTask; },
                    (_, _) => { clicks++; return Task.CompletedTask; }, () => store.ReserveRetry(account, process), _ => { }, CancellationToken.None).GetAwaiter().GetResult();
                check(delay == TimeSpan.FromSeconds(30 * (1 << attempt)), "friend-list Retry uses increasing 30/60/120-second backoff across new worker instances");
            }
            check(clicks == 3 && waits.Count(t => t >= TimeSpan.FromSeconds(30)) == 3, "each confirmed attempt waits once and clicks Retry once");
            var reopened = new FriendListRetryStore(root);
            try
            {
                FriendListRecovery.RetryAsync(dialog, _ => Task.FromResult(dialog), (_, _) => Task.CompletedTask,
                    (_, _) => { clicks++; return Task.CompletedTask; }, () => reopened.ReserveRetry(account, process), _ => { }, CancellationToken.None).GetAwaiter().GetResult();
                check(false, "fourth friend-list Retry must stop");
            }
            catch (FriendListRecoveryException) { check(clicks == 3 && reopened.Attempts(account, process) == 3, "restart cannot renew an exhausted budget or send a fourth click"); }
            check(reopened.Attempts(other, process) == 0, "one account's exhaustion cannot consume another account's budget");
            check(reopened.ReserveRetry(other, process) == TimeSpan.FromSeconds(30), "another account retains its independent initial delay");
            var replacement = process with { StartedUtc = process.StartedUtc.AddSeconds(1) };
            check(reopened.Attempts(account, replacement) == 0, "reused PID with a new creation time starts a new process budget");
            reopened.Clear(account, replacement);
            check(reopened.Attempts(account, process) == 3, "replacement evidence cannot clear the original process budget");
            reopened.Clear(account, process);
            check(reopened.Attempts(account, process) == 0 && reopened.Attempts(other, process) == 1, "verified recovery clears only its exact account/process budget");
            var read = 0;
            try
            {
                FriendListRecovery.RetryAsync(dialog, _ => Task.FromResult(++read == 1 ? dialog : wrongHeading), (_, _) => Task.CompletedTask,
                    (_, _) => { clicks++; return Task.CompletedTask; }, () => reopened.ReserveRetry(account, process), _ => { }, CancellationToken.None).GetAwaiter().GetResult();
                check(false, "changed dialog after waiting must stop");
            }
            catch (FriendListRecoveryException) { check(clicks == 3, "dialog changed during backoff sends no click or credential input"); }
            reopened.Clear(account, process);
            using var cancelled = new CancellationTokenSource();
            try
            {
                FriendListRecovery.RetryAsync(dialog, _ => Task.FromResult(dialog), (time, token) =>
                { if (time >= TimeSpan.FromSeconds(30)) cancelled.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
                    (_, _) => { clicks++; return Task.CompletedTask; }, () => reopened.ReserveRetry(account, process), _ => { }, cancelled.Token).GetAwaiter().GetResult();
                check(false, "cancelled backoff must stop");
            }
            catch (OperationCanceledException) { check(clicks == 3 && reopened.Attempts(account, process) == 1, "cancellation sends no input and cannot erase its reserved attempt"); }
            var path = Path.Combine(root, "friend-list-retries", account.ToString("N") + ".json");
            using (var held = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                try { reopened.ReserveRetry(account, process); check(false, "two helpers cannot reserve the same budget concurrently"); }
                catch (IOException) { check(reopened.Attempts(account, process) == 1, "a locked budget fails closed without renewing or consuming an attempt"); }
            }
            File.WriteAllText(path, "{invalid");
            try { reopened.ReserveRetry(account, process); check(false, "corrupt retry history cannot silently reset the limit"); }
            catch (JsonException) { check(true, "corrupt history fails closed before Retry"); }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
