using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FFXILauncher.Core;

public enum AlertEvent { Maintenance, Death, RecoveryFailed, MeritsCapped, CapacityCapped, JobPointsCapped, MasterLevel }

public sealed record NotificationProfile(Guid Id, string Name, List<AlertEvent> Events, int MaintenanceHours = 12)
{
    public static readonly Guid DefaultId = Guid.Parse("086d2834-3808-462d-907c-8aa7d72b0291");
    public static NotificationProfile Default => new(DefaultId, "All major events", Enum.GetValues<AlertEvent>().ToList());
    public override string ToString() => Name;
}

public sealed record NotificationSettings
{
    public bool TelegramEnabled { get; init; }
    public string TelegramChatId { get; init; } = "";
    public List<NotificationProfile> Profiles { get; init; } = [NotificationProfile.Default];
    public NotificationProfile? Profile(AccountConfiguration account) => Profiles.FirstOrDefault(p => p.Id == account.NotificationProfileId);
}

public sealed record CharacterAlert(AlertEvent Event, string Key, string Message);

// Track observations even when delivery is disabled, so enabling a profile cannot
// replay master levels. Current deaths and caps may alert on first observation.
public sealed class CharacterAlertTracker
{
    private readonly Dictionary<(Guid Account, string Nonce, string Job), int> masterLevels = [];
    private readonly Dictionary<Guid, HashSet<string>> active = [];
    private readonly Dictionary<Guid, HashSet<string>> maintenanceSent = [];
    private readonly HashSet<Guid> failedRecovery = [];

    public IReadOnlyList<CharacterAlert> Observe(AccountConfiguration account, CharacterState state, bool live)
    {
        if (!live) return [];
        var events = new List<CharacterAlert>();
        var conditions = new HashSet<string>();
        void Condition(bool? on, AlertEvent type, string key, string message)
        {
            if (on != true)
            {
                if (on == null && active.TryGetValue(account.Id, out var prior) && prior.Contains(key)) conditions.Add(key);
                return;
            }
            conditions.Add(key);
            if (!active.TryGetValue(account.Id, out var keys) || !keys.Contains(key)) events.Add(new(type, key, message));
        }
        Condition(state.DeathConfirmed, AlertEvent.Death, "death", $"Character died in {state.Zone}.");
        var points = state.Points;
        var job = state.Jobs?.MainJob ?? "unknown job";
        Condition(points?.Merits is {} merits && points.MeritCap is > 0 ? merits >= points.MeritCap : null,
            AlertEvent.MeritsCapped, "merits", "Merit points are capped.");
        // CP converts automatically into JP. The stored CP ceiling is 29,999;
        // the conversion target displayed to the user is 30,000.
        Condition(points?.Capacity is {} cp ? cp >= CharacterPoints.CapacityMaximum : null, AlertEvent.CapacityCapped, "cp:" + job, $"Capacity points capped on {job}.");
        Condition(points?.JobPoints is {} jp ? jp >= CharacterPoints.JobPointMaximum : null, AlertEvent.JobPointsCapped, "jp:" + job, $"Job points capped on {job}.");
        active[account.Id] = conditions;
        if (state.Jobs?.MasterLevel is {} after)
        {
            var identity = (account.Id, state.Nonce, job);
            if (masterLevels.TryGetValue(identity, out var before) && after > before)
                for (var level = before + 1; level <= after; level++)
                    events.Add(new(AlertEvent.MasterLevel, $"ml:{job}:{level}:{state.Nonce}", $"Master level {level} earned on {job}."));
            masterLevels[identity] = Math.Max(masterLevels.GetValueOrDefault(identity), after);
        }
        return events;
    }

    public IReadOnlyList<CharacterAlert> Maintenance(AccountConfiguration account, NotificationProfile profile, MaintenanceSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.CheckedUtc == null || now - snapshot.CheckedUtc > TimeSpan.FromMinutes(15) || snapshot.ReviewRequired?.Count > 0) return [];
        if (!maintenanceSent.TryGetValue(account.Id, out var sent)) maintenanceSent[account.Id] = sent = [];
        var events = new List<CharacterAlert>();
        foreach (var window in MaintenanceSchedule.UpcomingForCharacter(snapshot.Windows, account.World, now)
                     .Where(w => w.StartUtc <= now.AddHours(Math.Clamp(profile.MaintenanceHours, 0, 168))))
        {
            var key = $"maintenance:{window.World}:{window.StartUtc.ToUnixTimeSeconds()}:{window.EndUtc.ToUnixTimeSeconds()}";
            if (sent.Add(key)) events.Add(new(AlertEvent.Maintenance, key,
                $"{window.Title} · {MaintenanceTimeDisplay.Range(window.StartUtc, window.EndUtc)}"));
        }
        return events;
    }

    public CharacterAlert? RecoveryFailed(Guid id) => failedRecovery.Add(id)
        ? new(AlertEvent.RecoveryFailed, "recovery-failed", "Session dropped and automatic recovery failed or needs attention. Check the launcher.") : null;
    public void Recovered(Guid id) => failedRecovery.Remove(id);
}

public interface INotificationSender
{
    Task SendAsync(string token, string chatId, string message, CancellationToken cancellation);
}

public sealed class TelegramNotificationSender(HttpClient client) : INotificationSender
{
    public static void Validate(string token, string chatId)
    {
        if (!Regex.IsMatch(token, @"^[0-9]+:[A-Za-z0-9_-]{20,}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw new ArgumentException("Enter the bot token supplied by BotFather.");
        if (!Regex.IsMatch(chatId, @"^(?:-?[0-9]+|@[A-Za-z0-9_]{5,})$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
            throw new ArgumentException("Enter a numeric chat ID or @channel name.");
    }
    public async Task<IReadOnlyList<TelegramChat>> FindChatsAsync(string token, CancellationToken cancellation)
    {
        Validate(token, "0");
        try
        {
            using var response = await client.GetAsync($"https://api.telegram.org/bot{token}/getUpdates?limit=100&timeout=0", cancellation);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Could not read recent bot chats. Check the token, or enter the chat ID manually if this bot uses a webhook.");
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            if (!body.RootElement.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True ||
                !body.RootElement.TryGetProperty("result", out var updates) || updates.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Telegram did not return recent chats.");
            var chats = new Dictionary<string, TelegramChat>();
            foreach (var update in updates.EnumerateArray())
                foreach (var key in new[] { "message", "edited_message", "channel_post", "edited_channel_post", "my_chat_member" })
                    if (update.TryGetProperty(key, out var message) && message.TryGetProperty("chat", out var chat) &&
                        chat.TryGetProperty("id", out var id) && id.TryGetInt64(out var number))
                    {
                        var chatId = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        var name = chat.TryGetProperty("title", out var title) ? title.GetString() :
                            chat.TryGetProperty("first_name", out var first) ? first.GetString() : null;
                        chats[chatId] = new(chatId, name ?? chatId);
                    }
            return chats.Values.ToArray();
        }
        catch (HttpRequestException) { throw new InvalidOperationException("Telegram could not be reached. Check your connection."); }
        catch (JsonException) { throw new InvalidOperationException("Telegram returned an unreadable response."); }
    }
    public async Task SendAsync(string token, string chatId, string message, CancellationToken cancellation)
    {
        Validate(token, chatId);
        try
        {
            using var response = await client.PostAsJsonAsync($"https://api.telegram.org/bot{token}/sendMessage",
                new { chat_id = chatId, text = message, disable_notification = false }, cancellation);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Telegram delivery failed (HTTP {(int)response.StatusCode}). Check the bot token, chat ID and bot access.");
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellation), cancellationToken: cancellation);
            if (!body.RootElement.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
                throw new InvalidOperationException("Telegram did not accept the notification.");
        }
        // Never expose an exception containing the request URL: it includes the bot token.
        catch (HttpRequestException) { throw new InvalidOperationException("Telegram could not be reached. Check your connection."); }
        catch (JsonException) { throw new InvalidOperationException("Telegram returned an unreadable response."); }
    }
}

public sealed record TelegramChat(string Id, string Name)
{
    public override string ToString() => $"{Name} ({Id})";
}
