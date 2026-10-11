using System.Security.Cryptography;
using FFXILauncher.Core;

namespace FFXILauncher.Desktop.Platform;

internal sealed record CalibrationRequest(string Token, string Nonce, Guid Account, ManagedProcess Process, string Operation, DateTimeOffset ExpiresUtc, int PressMs = 80, int PollMs = 50, int MenuGapMs = 0);
internal sealed record CalibrationOutcome(bool Success, string Message, bool Validated = false, ManagedProcess? Process = null);
internal sealed record CalibrationReply(string Nonce, bool Finished, bool Success, string Message, bool Validated = false, ManagedProcess? Process = null);

// Same-user, capability-authenticated, bounded operations on tracked accounts.
// No shell commands, arbitrary paths, or untracked process termination.
internal sealed class WindowsCalibrationControl : IDisposable
{
    private readonly string directory, token;
    private readonly CancellationTokenSource stopped = new();
    private readonly Task loop;
    private string? activeNonce;
    private readonly object replyGate = new();
    public string CapturePath => Path.Combine(directory, "client.bmp");
    public WindowsCalibrationControl(string root, Func<CalibrationRequest, CancellationToken, Task<CalibrationOutcome>> handle)
    {
        directory = Path.Combine(root, "login-control");
        WindowsPrivateDirectory.Create(directory);
        foreach (var name in new[] { "request.json", "reply.json", "cancel", "client.bmp" }) File.Delete(Path.Combine(directory, name));
        token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        File.WriteAllText(Path.Combine(directory, "token"), token);
        loop = Task.Run(async () =>
        {
            string? handled = null;
            while (!stopped.IsCancellationRequested)
            {
                var request = WindowsAutoLogin.ReadJson<CalibrationRequest>(Path.Combine(directory, "request.json"));
                if (Accepts(request, token, DateTimeOffset.UtcNow) && request!.Nonce != handled)
                {
                    handled = request.Nonce;
                    lock (replyGate) activeNonce = request.Nonce;
                    using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(stopped.Token);
                    cancelled.CancelAfter(TimeSpan.FromMinutes(8));
                    using var monitor = new System.Threading.Timer(_ =>
                    {
                        try { if (File.Exists(Path.Combine(directory, "cancel")) && File.ReadAllText(Path.Combine(directory, "cancel")) == request.Nonce) cancelled.Cancel(); }
                        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
                    }, null, 100, 100);
                    Report("Checking the configured account and owned POL session…");
                    CalibrationOutcome result;
                    try { result = await handle(request, cancelled.Token); }
                    catch (OperationCanceledException) { result = new(false, "Calibration cancelled."); }
                    catch { result = new(false, "Calibration stopped. Inspect the managed POL window."); }
                    lock (replyGate)
                    {
                        WindowsAutoLogin.WriteJson(Path.Combine(directory, "reply.json"), new CalibrationReply(request.Nonce, true, result.Success, result.Message, result.Validated, result.Process));
                        activeNonce = null;
                    }
                }
                try { await Task.Delay(200, stopped.Token); } catch (OperationCanceledException) { break; }
            }
        });
    }
    internal static bool Accepts(CalibrationRequest? request, string token, DateTimeOffset now) => request != null &&
        request.Token == token && Guid.TryParseExact(request.Nonce, "N", out _) && request.Operation is "inspect" or "login" or "train-member" or "train-password" or "train-play" or "train-character" or "continue-login" or "return-game-menu" or "train-character-success" or "launch" or "close" or "shutdown" or "shutdown_all" &&
        request.Account != Guid.Empty && request.PressMs is >= 1 and <= 80 && request.PollMs is >= 1 and <= 50 &&
        (request.Operation is "train-member" or "train-character" or "train-character-success" || request.PressMs == 80 && request.PollMs == 50) &&
        (request.Operation == "train-character-success" ? request.MenuGapMs is >= 0 and <= 3000 : request.MenuGapMs == 0) &&
        (request.Operation == "launch" || request.Process != null && request.Process.Pid > 0) && request.ExpiresUtc > now && request.ExpiresUtc <= now.AddMinutes(1);
    public void Report(string message)
    {
        lock (replyGate)
            if (activeNonce is {} nonce) WindowsAutoLogin.WriteJson(Path.Combine(directory, "reply.json"), new CalibrationReply(nonce, false, false, message));
    }
    public void Dispose() { stopped.Cancel(); File.Delete(Path.Combine(directory, "token")); }
}
