using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FFXILauncher.Core;

// A training run may use credentials once per account, across restarts/builds.
// Post-authentication routes are tied to the exact authenticated process.
public sealed class LoginTrainingLedger(string directory)
{
    private sealed record Proof(Guid Account, ManagedProcess Process, string Binding);
    private string FileFor(Guid account, string suffix) => Path.Combine(directory, account.ToString("N") + suffix);
    public bool CredentialEntryUsed(Guid account) => account != Guid.Empty && File.Exists(FileFor(account, ".credentials-used"));
    public bool ReserveCredentialEntry(Guid account)
    {
        if (account == Guid.Empty) throw new ArgumentException("Account required.", nameof(account));
        Directory.CreateDirectory(directory);
        var path = FileFor(account, ".credentials-used");
        try { using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); return true; }
        catch (IOException) when (File.Exists(path)) { return false; }
    }
    public static string IdentityBinding(string identity) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    public void RecordAuthenticated(Guid account, ManagedProcess process, string binding)
    {
        if (!File.Exists(FileFor(account, ".credentials-used"))) throw new InvalidOperationException("Credential entry was not reserved.");
        var path = FileFor(account, ".session.json");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new Proof(account, process, binding)));
        File.Move(temporary, path, true);
    }
    public bool MatchesAuthenticated(Guid account, ManagedProcess process, string binding)
    {
        try
        {
            var path = FileFor(account, ".session.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 8192) return false;
            var proof = JsonSerializer.Deserialize<Proof>(File.ReadAllText(path));
            return proof?.Account == account && proof.Process == process && proof.Binding == binding;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }
}
