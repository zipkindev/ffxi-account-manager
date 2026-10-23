using System.Security.Cryptography;
using System.Text;
using FFXILauncher.Core;

namespace FFXILauncher.Desktop.Platform;

// A successful full login applies only to this account mapping and this automation build.
internal static class WindowsLoginValidation
{
    private sealed record Proof(string Binding, string Build, DateTimeOffset VerifiedUtc);
    private static string Binding(AccountConfiguration account) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        System.Text.Json.JsonSerializer.Serialize(new { account.Id, account.SquareEnixId, account.Character, account.World, account.Profile, account.PolPasswordStoredInPol, account.MemberPasswordRequired, account.UsesOneTimePassword }))));
    private static readonly Lazy<string> BuildHash = new(() => AppPayload.BuildIdentity);
    private static string Build => BuildHash.Value;
    private static string ProofPath(string directory, AccountConfiguration account) => Path.Combine(directory, "login-validation", account.Id.ToString("N") + ".json");
    public static bool IsValidated(string directory, AccountConfiguration account)
    {
        try { var proof = WindowsAutoLogin.ReadJson<Proof>(ProofPath(directory, account)); return proof != null && proof.Binding == Binding(account) && proof.Build == Build && proof.VerifiedUtc <= DateTimeOffset.UtcNow; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
    // A confirmed disconnect may validate the same previously tested mapping
    // through a full fresh login after an app update. This does not authorize
    // ordinary unattended launches with an older build's proof.
    public static bool HasPriorValidation(string directory, AccountConfiguration account)
    {
        try
        {
            var proof = WindowsAutoLogin.ReadJson<Proof>(ProofPath(directory, account));
            return proof != null && proof.Binding == Binding(account) && Guid.TryParseExact(proof.Build, "N", out _) &&
                proof.VerifiedUtc <= DateTimeOffset.UtcNow;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
    public static void Record(string directory, AccountConfiguration account)
    {
        WindowsPrivateDirectory.Create(Path.Combine(directory, "login-validation"));
        WindowsAutoLogin.WriteJson(ProofPath(directory, account), new Proof(Binding(account), Build, DateTimeOffset.UtcNow));
    }
}
