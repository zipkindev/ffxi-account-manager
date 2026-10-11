namespace FFXILauncher.Core;

public static class ShutdownCleanupPolicy
{
    public static readonly TimeSpan GracefulTimeout = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan ProcessExitTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan CountdownAllowance = TimeSpan.FromSeconds(45);
    public const int MaximumRetries = 2;
    public static bool ShouldRetry(bool freshGameHeartbeat, TimeSpan sinceLastDispatch, int retries) =>
        freshGameHeartbeat && sinceLastDispatch >= CountdownAllowance && retries < MaximumRetries;
    public static bool CanTerminate(bool dispatchConfirmed, TimeSpan elapsed, bool freshGameHeartbeat, TimeSpan? confirmedLogoutElapsed = null) =>
        dispatchConfirmed && !freshGameHeartbeat && (elapsed >= GracefulTimeout || confirmedLogoutElapsed >= ProcessExitTimeout);
}
