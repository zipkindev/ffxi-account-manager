namespace FFXILauncher.Core;

// Per-run authorization for reversible keyboard steps after exact member selection.
// This grants no authority to type or resubmit credentials.
public sealed class VerifiedPolEnterPath
{
    private readonly Dictionary<LoginCheckpoint, int> retries = [];
    public bool Started { get; private set; }
    public bool EditorRequested { get; private set; }
    public int RequestEditor(bool identityVerified)
    {
        if (!identityVerified || EditorRequested) throw new InvalidOperationException("Password editor activation requires verified identity and may occur only once.");
        EditorRequested = true;
        return 1;
    }
    public bool WaitingForEditor(LoginCheckpoint checkpoint, bool passwordEntered) =>
        EditorRequested && !passwordEntered && checkpoint is LoginCheckpoint.MemberInformation or LoginCheckpoint.Connect;
    public int Begin(bool exactMemberSelected)
    {
        if (!exactMemberSelected || Started) throw new InvalidOperationException("A fresh exact-ID member selection is required for the POL Enter path.");
        Started = true;
        return 1;
    }
    public bool TryRetry(LoginCheckpoint checkpoint, bool passwordEntered)
    {
        if (!Started || EditorRequested || passwordEntered || checkpoint != LoginCheckpoint.MemberInformation) return false;
        var count = retries.GetValueOrDefault(checkpoint);
        if (count >= 3) return false;
        retries[checkpoint] = count + 1;
        return true;
    }
}
