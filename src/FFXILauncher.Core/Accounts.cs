using System.Text.Json;

namespace FFXILauncher.Core;

public sealed record AccountConfiguration(Guid Id, string Label, string Profile, string Character,
    string World, string PolMember, string SquareEnixId, bool MaintainConnection = false, bool AutoLogin = false, string PlayOnlineId = "", bool ScheduleMaintenance = false, bool PolPasswordStoredInPol = true, bool MemberPasswordRequired = false, bool UsesOneTimePassword = false)
{
    public string? RecoveryPauseReason { get; init; }
    public const string ExternalSessionReason = "POL-1039: logged in from another terminal. Maintain connection turned off. Re-enable it after ending the other login.";
    public AccountConfiguration PauseForExternalSession() => this with { MaintainConnection = false, RecoveryPauseReason = ExternalSessionReason };
    [System.Text.Json.Serialization.JsonIgnore]
    public bool AutoLoginEnabled => AutoLogin || MaintainConnection;
}
public sealed record LauncherConfiguration(string WindowerPath = "", string PolPath = "",
    List<AccountConfiguration>? Accounts = null, string Theme = "midnight", bool LandscapeCards = true,
    Dictionary<Guid, string>? CharacterArtwork = null, List<string>? ArtworkRemaining = null, bool ShowCardMaintenance = true, int CardMaintenanceHours = 12, bool NormalizePolWindow = true)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public List<AccountConfiguration> ConfiguredAccounts => Accounts ?? [];
}
public sealed record AccountCredentials(string PolPassword, string SquareEnixPassword, string MemberPassword = "");
public interface ICredentialVault
{
    bool IsAvailable { get; }
    bool Contains(Guid accountId);
    void Save(Guid accountId, AccountCredentials credentials);
    AccountCredentials? Read(Guid accountId);
    void Delete(Guid accountId);
}

public sealed class ConfigurationStore(string directory)
{
    private readonly string path = Path.Combine(directory, "accounts.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public LauncherConfiguration Load()
    {
        if (!File.Exists(path)) return new();
        var configuration = JsonSerializer.Deserialize<LauncherConfiguration>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("The account configuration is empty.");
        if (configuration.ConfiguredAccounts.Select(a => a.Id).Distinct().Count() != configuration.ConfiguredAccounts.Count)
            throw new InvalidDataException("The account configuration contains duplicate identities.");
        return configuration;
    }
    public void Save(LauncherConfiguration configuration)
    {
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(configuration, Options));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public static class AccountIdentity
{
    public static bool Conflicts(AccountConfiguration left, AccountConfiguration right) =>
        left.Id == right.Id ||
        (!string.IsNullOrWhiteSpace(left.PlayOnlineId) && left.PlayOnlineId.Equals(right.PlayOnlineId, StringComparison.OrdinalIgnoreCase)) ||
        (!string.IsNullOrWhiteSpace(left.World) && left.World.Equals(right.World, StringComparison.OrdinalIgnoreCase) && left.Character.Equals(right.Character, StringComparison.OrdinalIgnoreCase));
}
