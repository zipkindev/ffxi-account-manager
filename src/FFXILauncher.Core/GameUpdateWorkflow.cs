using System.Text.RegularExpressions;

namespace FFXILauncher.Core;

public enum GameUpdateStage { None, Available, Confirmation, Checking, Downloading, Installing, Complete, RestartRequired, Error }
public sealed record GameUpdateScreen(GameUpdateStage Stage, string? Button = null, string Progress = "");

// Conservative updater recognition: news, account pages and generic Next/OK
// controls do not authorize input. Live layouts still require a maintenance trial.
public sealed class GameUpdateWorkflow
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan StalledAfter = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan MaximumDuration = TimeSpan.FromHours(2);
    public static readonly TimeSpan MaximumLoginDuration = MaximumDuration + TimeSpan.FromMinutes(8);
    private readonly DateTimeOffset started;
    private DateTimeOffset advanced;
    private GameUpdateScreen? last;
    private GameUpdateStage? acted;
    public GameUpdateWorkflow(DateTimeOffset now) { started = advanced = now; }
    public static GameUpdateScreen Recognize(LoginFrame frame)
    {
        var heading = frame.Lines.Any(l => LoginFrame.Normalize(l.Text) is "versionupdate" or "finalfantasyxiversionupdate" or "playonlineviewerversionupdate");
        if (!heading) return new(GameUpdateStage.None);
        if (frame.Has("Error code") || LoginCheckpoints.IsCredentialRejection(frame) || frame.Has("Connection failed") || frame.Has("Update failed")) return new(GameUpdateStage.Error);
        if (frame.Has("File Repair") || frame.Has("Uninstall")) return new(GameUpdateStage.Error);
        if (frame.Has("restart PlayOnline") || frame.Has("restart the PlayOnline Viewer") || frame.Has("restart your computer")) return new(GameUpdateStage.RestartRequired);
        string? Button(params string[] labels) => labels.FirstOrDefault(label => Control(frame, label) != null);
        if (frame.Has("version update is complete") || frame.Has("version update has been completed") || frame.Has("version update complete") || frame.Has("update completed") || frame.Has("version update was completed") || frame.Lines.Any(l => LoginFrame.Normalize(l.Text) == "updatecomplete"))
            return new(GameUpdateStage.Complete, Button("Next", "OK", "Play"));
        if ((frame.Has("Do you wish to begin the version update") || frame.Has("Do you want to start the version update")) && Control(frame, "No") != null)
            return new(GameUpdateStage.Confirmation, Button("Yes"));
        var progress = string.Join('|', Regex.Matches(frame.Text, @"\d+(?:\.\d+)?\s*%|\d+\s*(?:/|of)\s*\d+|[a-zA-Z0-9_.-]+\.(?:dat|dll|exe|bin)", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)).Select(m => m.Value));
        if (frame.Has("Downloading")) return new(GameUpdateStage.Downloading, Progress: progress);
        if (frame.Has("Installing") || frame.Has("Updating files")) return new(GameUpdateStage.Installing, Progress: progress);
        if (frame.Has("Checking files") || frame.Has("Checking file") || frame.Has("Checking version")) return new(GameUpdateStage.Checking, Progress: progress);
        if (frame.Has("version update is necessary") || frame.Has("version update is required") || frame.Has("new version is available") || Control(frame, "Update") != null)
            return new(GameUpdateStage.Available, Button("Update", "Next"));
        return new(GameUpdateStage.None);
    }
    public static bool MayClaim(ManagedProcess current, ManagedProcess? previous, bool previousStillRunning) =>
        previous == null || !previousStillRunning || previous == current;
    public static OcrLine? Control(LoginFrame frame, string label)
    {
        var line = frame.UniqueLine(label);
        return line is { Words.Length: > 0 } && frame.Width > 0 && frame.Height > 0 && line.Words.All(w =>
            double.IsFinite(w.X) && double.IsFinite(w.Y) && double.IsFinite(w.Width) && double.IsFinite(w.Height) &&
            w.Width > 0 && w.Height > 0 && w.X >= frame.Width * .1 && w.X + w.Width <= frame.Width * .98 &&
            w.Y >= frame.Height * .3 && w.Y + w.Height <= frame.Height * .98) ? line : null;
    }
    public void Observe(GameUpdateScreen screen, DateTimeOffset now)
    {
        if (now < started || now - started >= MaximumDuration) throw new InvalidOperationException("Game update exceeded its two-hour limit; POL remains open for inspection.");
        if (screen.Stage == GameUpdateStage.None)
        {
            if (now - advanced >= StalledAfter) throw new InvalidOperationException("Updater screen remained unrecognized for 15 minutes; POL remains open for inspection.");
            return; // An OCR miss cannot reset progress or permit another click.
        }
        if (last == null || last.Stage != screen.Stage || screen.Progress.Length > 0 && last.Progress != screen.Progress)
        { advanced = now; if (last?.Stage != screen.Stage) acted = null; }
        last = screen;
        if (now - advanced >= StalledAfter) throw new InvalidOperationException("Game update has shown no measurable progress for 15 minutes; POL remains open for inspection.");
    }
    public bool ShouldClick(GameUpdateScreen screen, bool otherGamesRunning) => !otherGamesRunning && screen.Button != null && acted != screen.Stage &&
        screen.Stage is GameUpdateStage.Available or GameUpdateStage.Confirmation or GameUpdateStage.Complete;
    public void ActionSent(GameUpdateScreen screen) => acted = screen.Stage;
}
