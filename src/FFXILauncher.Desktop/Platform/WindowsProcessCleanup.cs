using System.Diagnostics;

namespace FFXILauncher.Desktop.Platform;

public static class WindowsProcessCleanup
{
    public static async Task TerminateAsync(FFXILauncher.Core.ManagedProcess expected, string expectedExecutable, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (string.IsNullOrWhiteSpace(expectedExecutable)) throw new InvalidOperationException("The POL executable path must be configured before forced cleanup.");
        Process current;
        try { current = Process.GetProcessById(expected.Pid); }
        catch (ArgumentException) { return; }
        using (current)
        {
            // Pin the OS process handle before checking identity. PID reuse cannot
            // redirect a later termination to a replacement process.
            _ = current.SafeHandle;
            if (current.HasExited) return;
            if (current.StartTime.ToUniversalTime() != expected.StartedUtc)
                throw new InvalidOperationException("The PID belongs to a replacement process; cleanup refused.");
            if (!WindowsSessionPlatform.ExecutablePath(expected.Pid).Equals(Path.GetFullPath(expectedExecutable), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The process executable does not match the configured POL installation; cleanup refused.");
            cancellationToken.ThrowIfCancellationRequested();
            current.Kill(entireProcessTree: false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try { await current.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new TimeoutException("Windows has not confirmed process exit after forced cleanup. Startup remains blocked."); }
            if (!current.HasExited) throw new IOException("Process exit could not be confirmed. Startup remains blocked.");
        }
    }
}
