using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using FFXILauncher.Core;

namespace FFXILauncher.Desktop.Platform;

public sealed class WindowsCredentialVault(string directory) : ICredentialVault
{
    public bool IsAvailable => OperatingSystem.IsWindows();
    private string PathFor(Guid id) => Path.Combine(directory, id.ToString("N") + ".credentials");
    public bool Contains(Guid accountId) => File.Exists(PathFor(accountId));
    public void Save(Guid accountId, AccountCredentials credentials)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Credential storage is available in the Windows build.");
        SaveWindows(accountId, credentials);
    }
    [SupportedOSPlatform("windows")]
    private void SaveWindows(Guid id, AccountCredentials credentials)
    {
        Directory.CreateDirectory(directory);
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new InvalidOperationException("Windows user identity is unavailable.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(directory).SetAccessControl(security);
        var plain = JsonSerializer.SerializeToUtf8Bytes(credentials);
        var temporary = PathFor(id) + ".tmp";
        try
        {
            var encrypted = ProtectedData.Protect(plain, id.ToByteArray(), DataProtectionScope.CurrentUser);
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, PathFor(id), true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
    public AccountCredentials? Read(Guid accountId)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        return ReadWindows(accountId);
    }
    [SupportedOSPlatform("windows")]
    private AccountCredentials? ReadWindows(Guid id)
    {
        if (!Contains(id)) return null;
        var plain = ProtectedData.Unprotect(File.ReadAllBytes(PathFor(id)), id.ToByteArray(), DataProtectionScope.CurrentUser);
        try { return JsonSerializer.Deserialize<AccountCredentials>(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public void Delete(Guid accountId) => File.Delete(PathFor(accountId));
}
