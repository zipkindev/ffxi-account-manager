using System.Text.Json;
using System.Text.RegularExpressions;
using FFXILauncher.Core;

namespace FFXILauncher.Desktop.Platform;
public sealed class MaintenanceMonitor(string directory, HttpMessageHandler? handler = null) : IDisposable
{
    private readonly HttpClient client = handler == null
        ? new() { Timeout = TimeSpan.FromSeconds(15) }
        : new(handler) { Timeout = TimeSpan.FromSeconds(15) };
    public MaintenanceSnapshot Snapshot { get; private set; } = new([], null);
    public string Status { get; private set; } = "Maintenance notices have not been checked.";
    public IReadOnlyList<string> Worlds { get; set; } = [];
    private DateTimeOffset nextCheck;
    private bool checking;
    private string CachePath => Path.Combine(directory, "maintenance.json");
    public void Load()
    {
        try { if (File.Exists(CachePath)) Snapshot = JsonSerializer.Deserialize<MaintenanceSnapshot>(File.ReadAllText(CachePath)) ?? Snapshot; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { Status = "Maintenance cache could not be read; refresh required."; }
    }
    public async Task RefreshAsync(bool force, CancellationToken cancellationToken)
    {
        if (checking || (!force && DateTimeOffset.UtcNow < nextCheck)) return;
        checking = true;
        nextCheck = DateTimeOffset.UtcNow.AddMinutes(5);
        try
        {
            var listUri = new Uri("https://www.playonline.com/ff11us/info/list_mnt.shtml");
            var html = await client.GetStringAsync(listUri, cancellationToken);
            var links = Regex.Matches(html, "href=[\"'](?<url>(?:[^\"']*/)?news[0-9]+\\.shtml)[\"'][^>]*>(?<title>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(1));
            var windows = Snapshot.Windows.ToDictionary(w => w.Source + "|" + w.World);
            var review = new List<string>();
            foreach (Match link in links.Cast<Match>().Take(12))
            {
                var uri = new Uri(listUri, link.Groups["url"].Value);
                if (uri.Host != listUri.Host || uri.Scheme != "https") continue;
                var title = MaintenanceNoticeParser.PlainText(link.Groups["title"].Value).Trim();
                if (!title.Contains("Maintenance", StringComparison.OrdinalIgnoreCase) && !title.Contains("PlayOnline", StringComparison.OrdinalIgnoreCase)) continue;
                if (title.Contains("World Transfer", StringComparison.OrdinalIgnoreCase) || title.Contains("Forum", StringComparison.OrdinalIgnoreCase) || title.Contains("Website", StringComparison.OrdinalIgnoreCase)) continue;
                var notice = await client.GetStringAsync(uri, cancellationToken);
                var text = MaintenanceNoticeParser.PlainText(notice);
                var affected = Regex.Match(text, @"\[Affected Worlds?\](?<scope>.*?)(?:\[|$)", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)).Groups["scope"].Value;
                var scope = title + " " + affected;
                var allWorlds = scope.Contains("All Worlds", StringComparison.OrdinalIgnoreCase) || title.Contains("PlayOnline", StringComparison.OrdinalIgnoreCase);
                var targets = allWorlds ? new string?[] { null } : Worlds.Concat(WorldCatalog.Names).Distinct(StringComparer.OrdinalIgnoreCase).Where(w => !string.IsNullOrWhiteSpace(w) && Regex.IsMatch(scope, "\\b" + Regex.Escape(w) + "\\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))).Cast<string?>().ToArray();
                if (targets.Length == 0) { if (text.Contains("FINAL FANTASY XI", StringComparison.OrdinalIgnoreCase)) review.Add(title + " (world scope unrecognized)"); continue; }
                var parsed = targets.Select(world => MaintenanceNoticeParser.Parse(uri.AbsoluteUri, title, notice, world)).ToArray();
                if (parsed.Any(window => window == null)) { if (!review.Contains(title)) review.Add(title); continue; }
                foreach (var key in windows.Where(pair => pair.Value.Source == uri.AbsoluteUri).Select(pair => pair.Key).ToArray()) windows.Remove(key);
                foreach (var window in parsed) windows[window!.Source + "|" + window.World] = window;
            }
            if (links.Count == 0) throw new InvalidDataException("The official notice list format was not recognized.");
            Snapshot = new(windows.Values.Where(w => w.EndUtc > DateTimeOffset.UtcNow.AddDays(-7)).ToList(), DateTimeOffset.UtcNow, review);
            Directory.CreateDirectory(directory);
            var temporary = CachePath + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(Snapshot), cancellationToken);
            File.Move(temporary, CachePath, true);
            Status = $"Official notices checked at {DateTime.Now:t}. {review.Count} notice(s) need scope/time-format review.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        // InvalidDataException does not derive from IOException. In particular,
        // PlayOnline can redirect to a maintenance placeholder with HTTP 200.
        // Keep the previous check time so an unavailable feed cannot look fresh.
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException or TaskCanceledException or UnauthorizedAccessException or RegexMatchTimeoutException or UriFormatException)
        { Status = "Official notices unavailable; retaining cached windows. " + ex.Message; }
        finally { checking = false; }
    }
    public void Dispose() => client.Dispose();
}
