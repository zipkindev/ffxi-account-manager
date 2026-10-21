using FFXILauncher.Desktop.Platform;

internal static class FocusRecoveryRegression
{
    public static void Run()
    {
        var attempts = 0;
        var reports = 0;
        var recovery = new LoginFocusRecovery(() =>
        {
            if (++attempts < 3) throw new LoginFocusException("Fixture focus loss");
        }, _ => reports++);
        recovery.FocusAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (attempts != 3 || reports != 2) throw new Exception("Transient focus loss did not recover.");
        recovery.RecoverAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (attempts != 4 || reports != 3) throw new Exception("Later screen-capture focus loss did not use the shared budget.");
        try { recovery.RecoverAsync(CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("Focus budget was reset."); }
        catch (LoginFocusException) { }
        if (attempts != 4) throw new Exception("Exhausted recovery must stop without another focus request.");

        var rejectedAttempts = 0;
        var replaced = new LoginFocusRecovery(() => { rejectedAttempts++; throw new LoginSafetyException("Fixture replaced process"); }, _ => throw new Exception("Identity failure was retried."));
        try { replaced.FocusAsync(CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("Replaced process accepted."); }
        catch (LoginSafetyException) { }
        if (rejectedAttempts != 1) throw new Exception("Process identity failures must not be retried.");

        using var cancellation = new CancellationTokenSource();
        var cancelled = new LoginFocusRecovery(() => throw new LoginFocusException("Fixture focus loss"), _ => cancellation.Cancel());
        try { cancelled.FocusAsync(cancellation.Token).GetAwaiter().GetResult(); throw new Exception("Cancelled focus recovery continued."); }
        catch (OperationCanceledException) { }
        Console.WriteLine("PASS: transient focus recovery, shared retry limit, process-identity rejection and cancellation.");
    }
}
