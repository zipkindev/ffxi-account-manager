namespace FFXILauncher.Desktop.Platform;

// Shared budget for the entire login, not a new budget for each screen read.
// Only screen capture and initial focus use this; input actions are never replayed.
internal sealed class LoginFocusRecovery(Action focus, Action<string> report)
{
    private int remaining = 3;
    public async Task FocusAsync(CancellationToken cancellation)
    {
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            try { focus(); return; }
            catch (LoginFocusException) { await PrepareRecoveryAsync(cancellation); }
        }
    }
    public async Task RecoverAsync(CancellationToken cancellation)
    {
        await PrepareRecoveryAsync(cancellation);
        await FocusAsync(cancellation);
    }
    private async Task PrepareRecoveryAsync(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (remaining == 0)
            throw new LoginFocusException("Login paused: POL could not stay in front. Click Launch profile to retry.");
        remaining--;
        report("Bringing POL back to the front to continue login…");
        await Task.Delay(250, cancellation);
    }
}
