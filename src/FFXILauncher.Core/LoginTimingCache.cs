using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace FFXILauncher.Core;

// Identity remains a live check. A bound path proven by two full successful
// logins may replay the learned default-character Enter pair; actual character
// and world must still be verified online before completion.
public sealed class LoginTimingCache(string directory, string binding)
{
    public static string ProfileSettingsBinding(string path, string profile)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = true, MaxCharactersInDocument = 1_000_000 });
        var profiles = XDocument.Load(reader).Descendants("profile")
            .Where(p => (p.Attribute("name")?.Value ?? "").Equals(profile, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (profiles.Length != 1) throw new InvalidDataException("The timing-cache profile cannot be resolved uniquely.");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profiles[0].ToString(SaveOptions.DisableFormatting))));
    }
    public sealed record CharacterPath(int Width, int Height, double[] ReadyMilliseconds)
    {
        public TimeSpan Delay => TimeSpan.FromMilliseconds(ReadyMilliseconds.Max() + 100);
    }
    private sealed record Snapshot(string Binding, Dictionary<LoginCheckpoint, double[]> Timings, CharacterPath? CharacterSelection = null);
    private readonly Dictionary<LoginCheckpoint, double[]> learned = Load(directory, binding);
    private CharacterPath? characterPath = LoadPath(directory, binding);
    private CharacterPath? observedPath;
    private readonly Dictionary<LoginCheckpoint, double> observed = [];
    private static string CachePath(string directory, string binding) => Path.Combine(directory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(binding))) + ".json");

    private static Dictionary<LoginCheckpoint, double[]> Load(string directory, string binding)
    {
        try
        {
            var path = CachePath(directory, binding);
            if (!File.Exists(path) || new FileInfo(path).Length > 32768) return [];
            var snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path));
            if (snapshot?.Binding != binding || snapshot.Timings == null) return [];
            if (snapshot.Timings.Any(p => !Enum.IsDefined(p.Key) || p.Value == null || p.Value.Length > 3 ||
                p.Value.Any(v => !double.IsFinite(v) || v < 0 || v > 45000))) return [];
            return snapshot.Timings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }

    private static CharacterPath? LoadPath(string directory, string binding)
    {
        try
        {
            var path = CachePath(directory, binding);
            if (!File.Exists(path) || new FileInfo(path).Length > 32768) return null;
            var snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path));
            var candidate = snapshot?.CharacterSelection;
            return snapshot?.Binding == binding && candidate is { Width: >= 320 and <= 8192, Height: >= 240 and <= 8192 } &&
                candidate.ReadyMilliseconds is { Length: >= 1 and <= 3 } samples &&
                samples.All(v => double.IsFinite(v) && v is >= 0 and <= 44000) ? candidate : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    // Older polling hints never become an executable path. Two complete logins
    // must prove Title -> default-character Enter -> exact prompt -> world.
    public CharacterPath? ReplayPath => characterPath?.ReadyMilliseconds.Length >= 2 ? characterPath : null;

    public void ObserveCharacterPath(int width, int height, TimeSpan elapsed)
    {
        if (width is >= 320 and <= 8192 && height is >= 240 and <= 8192 && elapsed.TotalMilliseconds is >= 0 and <= 44000)
            observedPath = new(width, height, [elapsed.TotalMilliseconds]);
    }

    public void InvalidateReplayPath()
    {
        characterPath = null;
        observedPath = null;
        Save();
    }

    public void Observe(LoginCheckpoint from, TimeSpan elapsed)
    {
        if (from != LoginCheckpoint.Unknown && from != LoginCheckpoint.Error &&
            elapsed.TotalMilliseconds is >= 0 and <= 45000)
            observed[from] = Math.Min(observed.GetValueOrDefault(from, double.MaxValue), elapsed.TotalMilliseconds);
    }

    public TimeSpan LoadingPollDelay(LoginCheckpoint stage, TimeSpan elapsed)
    {
        // Only long, reversible loading transitions. Check at least every second,
        // and start normal reads two seconds before the fastest learned arrival.
        if (stage is not LoginCheckpoint.PlayConfirmation and not LoginCheckpoint.Title ||
            !learned.TryGetValue(stage, out var samples) || samples.Length < 2) return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds(Math.Clamp(samples.Min() - 2000 - elapsed.TotalMilliseconds, 0, 1000));
    }

    // Caller must establish a complete same-run login before committing hints.
    public void CommitSuccessfulRun()
    {
        foreach (var (stage, elapsed) in observed)
            learned[stage] = learned.GetValueOrDefault(stage, []).Append(elapsed).TakeLast(3).ToArray();
        if (observedPath != null)
        {
            var compatible = characterPath?.Width == observedPath.Width && characterPath.Height == observedPath.Height;
            characterPath = observedPath with { ReadyMilliseconds = (compatible ? characterPath!.ReadyMilliseconds : [])
                .Concat(observedPath.ReadyMilliseconds).TakeLast(3).ToArray() };
        }
        Save();
    }

    private void Save()
    {
        Directory.CreateDirectory(directory);
        var path = CachePath(directory, binding);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Snapshot(binding, learned, characterPath)));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
