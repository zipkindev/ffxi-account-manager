using System.Text;
using System.Text.RegularExpressions;

namespace FFXILauncher.Core;

public sealed record StartupScriptUpdate(byte[] Content, bool Changed);
public static class WindowerStartupScript
{
    public const string LoadCommand = "lua load FFXIManager";
    private static readonly Regex AlreadyLoaded = new(@"^\s*lua\s+(?:load|l)\s+(?:FFXIManager|""FFXIManager""|'FFXIManager')(?=\s*(?:;|//|$))", RegexOptions.IgnoreCase | RegexOptions.Multiline, TimeSpan.FromSeconds(1));
    public static StartupScriptUpdate EnsureAutoLoad(byte[] original)
    {
        Encoding encoding = new UTF8Encoding(false);
        var preamble = 0;
        // Preserve existing bytes and encoding, including legacy ANSI comments.
        foreach (var candidate in new Encoding[] { new UTF32Encoding(false, true), new UTF32Encoding(true, true), Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode })
        {
            var marker = candidate.GetPreamble();
            if (marker.Length == 0 || !original.AsSpan().StartsWith(marker)) continue;
            encoding = candidate; preamble = marker.Length; break;
        }
        var text = encoding.GetString(original, preamble, original.Length - preamble);
        if (AlreadyLoaded.IsMatch(text)) return new(original, false);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : text.Contains('\n') ? "\n" : text.Contains('\r') ? "\r" : "\r\n";
        var entry = encoding.GetBytes(LoadCommand + newline);
        var updated = new byte[original.Length + entry.Length];
        original.AsSpan(0, preamble).CopyTo(updated);
        entry.CopyTo(updated.AsSpan(preamble));
        original.AsSpan(preamble).CopyTo(updated.AsSpan(preamble + entry.Length));
        return new(updated, true);
    }
}
