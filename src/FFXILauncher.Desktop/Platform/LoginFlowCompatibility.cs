using System.Security.Cryptography;
using System.Text;

namespace FFXILauncher.Desktop.Platform;

// Hash login implementation inputs rather than the assembly MVID, app version,
// release notes or packaging. Normalize line endings across Windows/Linux builds.
internal static class LoginFlowCompatibility
{
    private const string Prefix = "FFXILauncher.LoginFlow.";
    private static readonly Lazy<string> CurrentHash = new(() =>
    {
        var assembly = typeof(LoginFlowCompatibility).Assembly;
        var names = assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
        if (names.Length != 15) throw new InvalidOperationException("Login compatibility inputs are incomplete.");
        var inputs = names.Select(name =>
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return new KeyValuePair<string, string>(name[Prefix.Length..], reader.ReadToEnd());
        });
        using var ocr = new StreamReader(assembly.GetManifestResourceStream("FFXILauncher.Payload.tools/windows-ocr.ps1")!, Encoding.UTF8);
        return Fingerprint(inputs.Append(new("tools/windows-ocr.ps1", ocr.ReadToEnd())));
    });
    internal static string Current => CurrentHash.Value;
    internal static string Fingerprint(IEnumerable<KeyValuePair<string, string>> inputs)
    {
        var manifest = new StringBuilder();
        foreach (var entry in inputs.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var normalized = entry.Value.Replace("\r\n", "\n", StringComparison.Ordinal);
            manifest.Append(entry.Key).Append('\n').Append(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))).Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest.ToString())));
    }

    // Audited deployed 1.0.26/1.0.28/1.0.29 builds: their completed full-login proofs
    // used the trained POL input route retained in this baseline. Disconnect
    // handling changed, but did not change credentials or character selection.
    // Only these exact build IDs may carry legacy proofs into this baseline;
    // a later login-source change automatically disables this bridge.
    private const string LegacyBaseline = "3A85092BB975A1D6C1234D668FAC1B8A22971850690E0D0A2ADF363D9DC40778";
    // 1.0.29's normal input route is unchanged; only the separately recognized
    // updater branch and its wait envelope were added.
    internal static bool AcceptsPriorFlow(string? flow) => Current == LegacyBaseline &&
        flow == "E12AB56C4CD3CB2612585B619F2F3D8420F9412FEE653290A0BAD7F0D71A4527";
    internal static bool AcceptsLegacyBuild(string? build) => Current == LegacyBaseline && build is
        "eaa4e971ee0140869898d88ea901d5ad" or
        "cf5fbeb42c504f57bbf3da94d0186a1c" or
        "b8d01e8893f7413e92677bda379cd5d3" or
        "f43fd3a67f2040a6b532e7988775b0fb";
}
