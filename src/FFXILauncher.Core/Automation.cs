namespace FFXILauncher.Core;

public enum LoginStage { Queued, PolMember, Authentication, FfxiTitle, CharacterSelection, WorldEntry, Online, NeedsAttention }
public sealed record ScreenEvidence(LoginStage Stage, bool Recognized, string? Character = null, string? World = null);

// Platform adapters supply evidence; a completed input call is never evidence of success.
public sealed class LoginWorkflow(string character, string world)
{
    public LoginStage Stage { get; private set; } = LoginStage.Queued;
    public bool Observe(ScreenEvidence evidence)
    {
        if (!evidence.Recognized || Stage == LoginStage.NeedsAttention) return false;
        var expected = Stage switch
        {
            LoginStage.Queued => LoginStage.PolMember,
            LoginStage.PolMember => LoginStage.Authentication,
            LoginStage.Authentication => LoginStage.FfxiTitle,
            LoginStage.FfxiTitle => LoginStage.CharacterSelection,
            LoginStage.CharacterSelection => LoginStage.WorldEntry,
            LoginStage.WorldEntry => LoginStage.Online,
            _ => Stage
        };
        if (evidence.Stage != expected) return false;
        if (expected is LoginStage.WorldEntry or LoginStage.Online &&
            (!string.Equals(character, evidence.Character, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(world, evidence.World, StringComparison.OrdinalIgnoreCase))) return false;
        Stage = expected;
        return true;
    }
    public void Pause() => Stage = LoginStage.NeedsAttention;
}

public readonly record struct Viewport(double Left, double Top, double Width, double Height)
{
    public (double X, double Y) Map(double referenceX, double referenceY, double referenceWidth, double referenceHeight)
    {
        if (!double.IsFinite(Left) || !double.IsFinite(Top) || !double.IsFinite(Width) || !double.IsFinite(Height) ||
            !double.IsFinite(referenceWidth) || !double.IsFinite(referenceHeight) || Width <= 0 || Height <= 0 ||
            referenceWidth <= 0 || referenceHeight <= 0 || !double.IsFinite(referenceX) || !double.IsFinite(referenceY) ||
            referenceX < 0 || referenceX >= referenceWidth || referenceY < 0 || referenceY >= referenceHeight)
            throw new ArgumentOutOfRangeException(nameof(referenceX), "Invalid viewport or reference coordinates.");
        return (Left + referenceX * Width / referenceWidth, Top + referenceY * Height / referenceHeight);
    }
}

public sealed record ManagedProcess(int Pid, DateTime StartedUtc);
public enum SessionHealth { AwaitingLogin, Responsive, Unresponsive, Exited, Unknown }
public interface ISessionPlatform
{
    bool CanLaunch { get; }
    Task<ManagedProcess> LaunchAsync(LauncherConfiguration configuration, AccountConfiguration account, IReadOnlyCollection<ManagedProcess> knownSessions, CancellationToken cancellationToken);
    SessionHealth Inspect(ManagedProcess process);
}
