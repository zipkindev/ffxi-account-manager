namespace FFXILauncher.Core;

public sealed class CharacterSessionBackoff
{
    public static readonly TimeSpan CrashRestartDelay = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);
    public const int MaximumRetries = 5;
    public static bool RequiresCrashDelay(bool confirmedDisconnect, bool scheduledMaintenance) => !confirmedDisconnect && !scheduledMaintenance;
    public int Attempts { get; private set; }
    public TimeSpan ReserveRetry()
    {
        if (Attempts >= MaximumRetries) throw new InvalidOperationException("The server still holds the previous character session after five retries. POL remains open; automatic input is paused.");
        Attempts++;
        return RetryDelay;
    }
    public static bool IsHeld(LoginFrame frame) => LoginCheckpoints.MaintenanceErrorCode(frame) == "FFXI-3201" &&
        frame.Has("Same character is already logged in") && frame.Has("Please wait a few minutes") && frame.Has("reconnect");
}
