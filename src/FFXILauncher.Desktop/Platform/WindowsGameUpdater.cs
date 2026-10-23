using System.Diagnostics;
using FFXILauncher.Core;

namespace FFXILauncher.Desktop.Platform;

internal sealed partial class WindowsLoginEngine
{
    private sealed record UpdateOwner(ManagedProcess Process, string PolPath);
    private static bool OwnerStillRunning(UpdateOwner owner)
    {
        try
        {
            using var p = Process.GetProcessById(owner.Process.Pid);
            return p.StartTime.ToUniversalTime() == owner.Process.StartedUtc &&
                string.Equals(WindowsSessionPlatform.ExecutablePath(p.Id), owner.PolPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return true; } // Uncertain ownership blocks another updater.
    }
    private bool OtherGamesMayUseInstallation()
    {
        foreach (var candidate in Process.GetProcessesByName("pol"))
        {
            using (candidate)
            {
                if (candidate.Id == process.Pid) continue;
                try
                {
                    if (!string.Equals(WindowsSessionPlatform.ExecutablePath(candidate.Id), config.PolPath, StringComparison.OrdinalIgnoreCase)) continue;
                    // Fresh POL login windows can queue behind the one updater.
                    // Character/game windows and ambiguous transitions block writes.
                    if (!candidate.MainWindowTitle.StartsWith("PlayOnline Viewer", StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return true; }
            }
        }
        return false;
    }
    private async Task RunGameUpdateAsync(LoginFrame initial, CancellationToken cancellation)
    {
        if (recoveringDisconnect) throw new LoginSafetyException("An update is required; complete a fresh POL launch after the game sessions exit.");
        using var updaterLock = new FileStream(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FFXILauncher", "game-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var ownerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FFXILauncher", "game-update-owner.json");
        var owner = WindowsAutoLogin.ReadJson<UpdateOwner>(ownerPath);
        if (File.Exists(ownerPath) && (owner?.Process == null || owner.Process.Pid <= 0 ||
            owner.Process.StartedUtc <= DateTime.UnixEpoch || owner.Process.StartedUtc > DateTime.UtcNow.AddSeconds(3) || string.IsNullOrWhiteSpace(owner.PolPath)))
            throw new LoginSafetyException("Updater ownership could not be read; no second update was started.");
        if (!GameUpdateWorkflow.MayClaim(process, owner?.Process, owner != null && OwnerStillRunning(owner)))
            throw new LoginSafetyException("Another POL process still owns an unfinished update. Finish that update before starting another; its process was preserved.");
        WindowsAutoLogin.WriteJson(ownerPath, new UpdateOwner(process, config.PolPath));
        var flow = new GameUpdateWorkflow(DateTimeOffset.UtcNow);
        var frame = initial;
        var seenUpdate = false;
        string? status = null;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var screen = GameUpdateWorkflow.Recognize(frame);
            if (LoginCheckpoints.Recognize(frame) == LoginCheckpoint.Error)
                throw new LoginSafetyException("POL reported an error during the update; automatic input stopped and the process remains open.");
            flow.Observe(screen, DateTimeOffset.UtcNow);
            if (screen.Stage == GameUpdateStage.Error) throw new LoginSafetyException("The updater reported an error. POL remains open; no repair or restart was attempted.");
            if (screen.Stage == GameUpdateStage.RestartRequired) throw new LoginSafetyException("The updater requires a PlayOnline or Windows restart. POL remains open for the maintenance trial; no reboot or process replacement was attempted.");
            if (screen.Stage == GameUpdateStage.None)
            {
                if (seenUpdate && LoginCheckpoints.Recognize(frame) is LoginCheckpoint.Home or LoginCheckpoint.Play or LoginCheckpoint.PlayConfirmation or LoginCheckpoint.Agreement or LoginCheckpoint.Title)
                {
                    File.Delete(ownerPath);
                    report("Updater returned to the login route; continuing to verify character and world…");
                    return;
                }
                // Partial/unfamiliar screens never authorize clicks or completion.
            }
            else seenUpdate = true;
            var blocked = OtherGamesMayUseInstallation();
            var next = blocked ? "Game update waiting for other game sessions to exit; running games are preserved." : "Game update: " + screen.Stage + ". Waiting for verified progress…";
            if (status != next) { report(next); status = next; }
            if (blocked)
            {
                // Wait without captures/focus recovery while other games run.
                await Task.Delay(GameUpdateWorkflow.PollInterval, cancellation);
                continue;
            }
            if (flow.ShouldClick(screen, blocked))
            {
                await Task.Delay(500, cancellation);
                var confirmedFrame = await ReadFrame(cancellation);
                var confirmed = GameUpdateWorkflow.Recognize(confirmedFrame);
                if (confirmed.Stage == screen.Stage && confirmed.Button == screen.Button && !OtherGamesMayUseInstallation())
                {
                    cancellation.ThrowIfCancellationRequested();
                    var dpi = WindowsLoginWindow.SetThreadDpiAwarenessContext(new(-4));
                    try { Click(confirmedFrame, confirmed.Button!); }
                    finally { if (dpi != IntPtr.Zero) WindowsLoginWindow.SetThreadDpiAwarenessContext(dpi); }
                    flow.ActionSent(confirmed);
                    report("Game update: selected " + confirmed.Button + " on " + confirmed.Stage + ".");
                }
            }
            await Task.Delay(GameUpdateWorkflow.PollInterval, cancellation);
            frame = await ReadFrame(cancellation);
        }
    }
}
