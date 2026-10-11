namespace FFXILauncher.Core;

public interface IAutoLoginAutomation
{
    string? ReadinessProblem(AccountConfiguration account);
    Task LoginAsync(AccountConfiguration account, ManagedProcess process, ICredentialVault vault, CancellationToken cancellationToken);
}
public sealed class UncalibratedAutoLogin : IAutoLoginAutomation
{
    public string? ReadinessProblem(AccountConfiguration account) => "Windows login checkpoints are not calibrated yet.";
    public Task LoginAsync(AccountConfiguration account, ManagedProcess process, ICredentialVault vault, CancellationToken cancellationToken)
        => Task.FromException(new InvalidOperationException(ReadinessProblem(account)));
}
