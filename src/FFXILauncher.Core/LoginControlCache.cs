using System.Security.Cryptography;
using System.Text.Json;

namespace FFXILauncher.Core;

// Bound control caches contain geometry and pixel digests, never recognized
// text, credentials or screenshots. Callers commit only verified routes.
public sealed class LoginControlCache(string directory, string binding)
{
    public sealed record Control(int Width, int Height, int X, int Y, int W, int H, string Digest, int Samples, double? CancelX = null, double? CancelY = null);
    private sealed record Tuning(int PressMs = 80, int PollMs = 50, int TrialPressMs = 80, int TrialPollMs = 50, int Passes = 0);
    private sealed record Snapshot(string Binding, Control[] Controls, Tuning? Timing = null);
    private readonly List<Control> controls = Load(directory, binding);
    private Tuning timing = LoadTuning(directory, binding);
    public int ReplayPressMs => timing.PressMs;
    public int ReplayPollMs => timing.PollMs;
    public bool HasReplay => controls.Any(c => c.Samples == 2);
    private Control? observed;
    private static string PathFor(string directory, string binding) => Path.Combine(directory,
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(binding))) + ".json");
    private static bool Valid(Control c) => c.Width is >= 320 and <= 8192 && c.Height is >= 240 and <= 8192 &&
        c.X >= 0 && c.Y >= 0 && c.W >= 8 && c.H >= 8 &&
        (c.CancelX == null ? c.W <= 200 && c.H <= 80 : c.W <= 1200 && c.H <= 240) &&
        c.X <= c.Width - c.W && c.Y <= c.Height - c.H && c.Samples is >= 1 and <= 2 &&
        c.Digest is { Length: 64 } && c.Digest.All(char.IsAsciiHexDigit) &&
        (c.CancelX == null && c.CancelY == null || c.CancelX is {} x && c.CancelY is {} y &&
            double.IsFinite(x) && double.IsFinite(y) && x > c.Width * .55 && x < c.Width * .8 && y >= c.Height * .5 && y <= c.Height * .65);
    private static Tuning LoadTuning(string directory, string binding)
    {
        try
        {
            var path = PathFor(directory, binding);
            if (!File.Exists(path) || new FileInfo(path).Length > 32768) return new();
            var snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path));
            return snapshot?.Binding == binding && snapshot.Timing is { PressMs: >= 1 and <= 80, PollMs: >= 1 and <= 50,
                TrialPressMs: >= 1 and <= 80, TrialPollMs: >= 1 and <= 50, Passes: >= 0 and <= 3 } valid ? valid : new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void RecordSectionTrial(int pressMs, int pollMs, bool accepted)
    {
        if (pressMs is < 1 or > 80 || pollMs is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(pressMs));
        var same = timing.TrialPressMs == pressMs && timing.TrialPollMs == pollMs;
        var passes = accepted ? Math.Min(3, same ? timing.Passes + 1 : 1) : 0;
        timing = timing with { TrialPressMs = pressMs, TrialPollMs = pollMs, Passes = passes };
        if (passes == 3) timing = timing with { PressMs = pressMs, PollMs = pollMs };
        else if (!accepted && timing.PressMs == pressMs && timing.PollMs == pollMs) timing = timing with { PressMs = 80, PollMs = 50 };
        Save();
    }
    private static List<Control> Load(string directory, string binding)
    {
        try
        {
            var path = PathFor(directory, binding);
            if (!File.Exists(path) || new FileInfo(path).Length > 32768) return [];
            var snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path));
            return snapshot?.Binding == binding && snapshot.Controls is { Length: <= 8 } &&
                snapshot.Controls.All(c => c != null && Valid(c)) ? snapshot.Controls.ToList() : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }
    private static string? Digest(byte[] bmp, Control c)
    {
        // The owned capture writer emits uncompressed top-down 32-bit BMP.
        // Reject other layouts; never interpret arbitrary offsets or sizes.
        if (!Valid(c) || bmp.Length < 54 || BitConverter.ToUInt16(bmp, 0) != 0x4d42 ||
            BitConverter.ToInt32(bmp, 10) != 54 || BitConverter.ToInt32(bmp, 14) != 40 ||
            BitConverter.ToInt32(bmp, 18) != c.Width || BitConverter.ToInt32(bmp, 22) != -c.Height ||
            BitConverter.ToUInt16(bmp, 26) != 1 || BitConverter.ToUInt16(bmp, 28) != 32 ||
            BitConverter.ToInt32(bmp, 30) != 0 || bmp.Length != 54L + (long)c.Width * c.Height * 4) return null;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var y = c.Y; y < c.Y + c.H; y++)
            for (var x = c.X; x < c.X + c.W; x++)
                hash.AppendData(bmp, 54 + (y * c.Width + x) * 4, 3); // Ignore unspecified alpha.
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static byte[]? ReadBitmap(string path)
    {
        var size = new FileInfo(path).Length;
        return size is >= 54 and <= 32_000_000 ? File.ReadAllBytes(path) : null;
    }
    public Control? MatchLogIn(string bitmapPath) => Match(bitmapPath, false);
    public Control? MatchCharacterConfirmation(string bitmapPath) => Match(bitmapPath, true);
    public bool ObservedConfirmationStillVisible(string bitmapPath)
    {
        if (observed is not { CancelX: not null }) return false;
        var bmp = ReadBitmap(bitmapPath);
        return bmp != null && Digest(bmp, observed) == observed.Digest;
    }
    private Control? Match(string bitmapPath, bool confirmation)
    {
        if (!controls.Any(c => c.Samples == 2)) return null;
        var bmp = ReadBitmap(bitmapPath);
        if (bmp == null) return null;
        return controls.FirstOrDefault(c => c.Samples == 2 && (c.CancelX != null) == confirmation && Digest(bmp, c) == c.Digest);
    }
    public void ObserveLogIn(LoginFrame frame, string bitmapPath)
    {
        var line = LoginCheckpoints.PolMenuButton(frame, "Log In") ?? frame.UniqueLine("Log In");
        if (line == null && LoginCheckpoints.PolLogInPoint(frame) is {} point)
        {
            // The highlighted caption may be unreadable. Observe the same bounded
            // button crop established by the independent menu anchors. Promotion
            // still requires successful runs; this never teaches a failed route.
            var scale = frame.Width / 640d;
            line = new OcrLine("Log In", [new("Log In", point.X - 32 * scale, point.Y - 12 * scale, 64 * scale, 24 * scale)]);
        }
        if (line == null || line.Words.Length == 0 || line.Words.Any(w => !double.IsFinite(w.X) || !double.IsFinite(w.Y) ||
            !double.IsFinite(w.Width) || !double.IsFinite(w.Height) || w.Width <= 0 || w.Height <= 0)) return;
        ObserveLine(frame, bitmapPath, line);
    }
    public void ObserveCharacterConfirmation(LoginFrame frame, string bitmapPath, string character)
    {
        if (!LoginCheckpoints.ConfirmsCharacter(frame, character) || LoginCheckpoints.ConfirmationCancelPoint(frame, character) is not {} cancel) return;
        var line = frame.Lines.FirstOrDefault(l => LoginFrame.Normalize(l.Text) == "loginwith" + LoginFrame.Normalize(character) && l.Words.Length > 0 &&
            l.Words.All(w => double.IsFinite(w.X) && double.IsFinite(w.Y) && double.IsFinite(w.Width) && double.IsFinite(w.Height) &&
                w.Width > 0 && w.Height > 0 && w.X >= frame.Width * .25 && w.X + w.Width <= frame.Width * .75 &&
                w.Y >= frame.Height * .35 && w.Y + w.Height <= frame.Height * .6));
        if (line == null || LoginCheckpoints.ConfirmationButton(frame, "Yes") is not { Words: [var yes] }) return;
        var scale = Math.Round(yes.Height / 14 * 4) / 4;
        if (!double.IsFinite(scale) || scale <= 0) return;
        // OCR's glyph boxes fluctuate during animation. The fixed paired
        // dialog layout gives a repeatable region containing the whole prompt.
        var x = (int)Math.Floor((yes.X + yes.Width / 2) / 16) * 16 - (int)(160 * scale);
        var y = (int)Math.Floor((yes.Y + yes.Height / 2 - 64 * scale) / 16) * 16 - (int)(32 * scale);
        var candidate = new Control(frame.Width, frame.Height, x, y, (int)(320 * scale), (int)(64 * scale), new string('0', 64), 1, cancel.X, cancel.Y);
        if (line.Words.Any(w => w.X < x || w.X + w.Width > x + candidate.W || w.Y < y || w.Y + w.Height > y + candidate.H)) return;
        ObserveCandidate(bitmapPath, candidate);
    }
    private void ObserveLine(LoginFrame frame, string bitmapPath, OcrLine line, double? cancelX = null, double? cancelY = null)
    {
        var x = (int)Math.Floor(line.Words.Min(w => w.X) / 8) * 8;
        var y = (int)Math.Floor(line.Words.Min(w => w.Y) / 8) * 8;
        var right = (int)Math.Ceiling(line.Words.Max(w => w.X + w.Width) / 8) * 8;
        var bottom = (int)Math.Ceiling(line.Words.Max(w => w.Y + w.Height) / 8) * 8;
        var candidate = new Control(frame.Width, frame.Height, x, y, right - x, bottom - y, new string('0', 64), 1, cancelX, cancelY);
        ObserveCandidate(bitmapPath, candidate);
    }
    private void ObserveCandidate(string bitmapPath, Control candidate)
    {
        var bmp = ReadBitmap(bitmapPath);
        var digest = bmp == null ? null : Digest(bmp, candidate);
        if (digest != null) observed = candidate with { Digest = digest };
    }
    public void CommitSuccessfulRun()
    {
        if (observed == null) return;
        var index = controls.FindIndex(c => c with { Samples = 1, CancelX = observed.CancelX, CancelY = observed.CancelY } == observed);
        if (index >= 0) controls[index] = controls[index] with { Samples = 2 };
        else { controls.Add(observed); if (controls.Count > 8) controls.RemoveAt(0); }
        Save();
        observed = null;
    }
    private void Save()
    {
        Directory.CreateDirectory(directory);
        var path = PathFor(directory, binding);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Snapshot(binding, controls.ToArray(), timing)));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
