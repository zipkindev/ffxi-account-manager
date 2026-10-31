using System.Net;
using System.Text.Json;
using FFXILauncher.Core;

internal static class NotificationRegression
{
    internal static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var account = new AccountConfiguration(Guid.NewGuid(), "Main", "", "Tester", "Odin", "", "");
        var state = new CharacterState("abcdef0123456789", now, 100, 100, 0, 0, false, 100, "Field", null,
            Jobs: new(10, "WAR", 99, "SAM", 49), Points: new(123, 56000, 74, 75, 12345, 499));
        var tracker = new CharacterAlertTracker();
        check(tracker.Observe(account, state, true).Count == 0, "initial healthy stats establish an alert baseline");
        var capped = state with { Points = state.Points! with { Merits = 75, Capacity = 29999, JobPoints = 500 } };
        check(tracker.Observe(account, capped, false).Count == 0, "stale stats cannot alert or alter baseline");
        check(tracker.Observe(account, capped, true).Select(a => a.Event).ToHashSet().SetEquals(
            new[] { AlertEvent.MeritsCapped, AlertEvent.CapacityCapped, AlertEvent.JobPointsCapped }), "all three point ceilings trigger alerts");
        check(tracker.Observe(account, capped, true).Count == 0, "repeated samples do not repeat cap alerts");
        tracker.Observe(account, capped with { Points = null }, true);
        check(tracker.Observe(account, capped, true).Count == 0, "temporarily missing point data does not rearm cap alerts");
        tracker.Observe(account, state, true);
        check(tracker.Observe(account, capped, true).Count == 3, "spending points rearms cap alerts");
        var dead = capped with { Hp = 0, IsDead = true };
        check(tracker.Observe(account, dead, true).Single().Event == AlertEvent.Death, "confirmed death alerts once");
        check(tracker.Observe(account, dead, true).Count == 0, "death is not repeated each polling tick");
        tracker.Observe(account, state with { ZoneId = 280, Zone = "Mog Garden", Hp = 0, IsDead = true }, true);
        check(tracker.Observe(account, state with { ZoneId = 280, Zone = "Mog Garden", Hp = 0, IsDead = true }, true).Count == 0, "safe-area zero HP cannot alert death");
        check(tracker.Observe(account, state with { Jobs = state.Jobs! with { MasterLevel = 12 } }, true).Count(a => a.Event == AlertEvent.MasterLevel) == 2, "skipped observations report every earned master level");
        tracker.Observe(account, state with { Jobs = state.Jobs! with { MasterLevel = 5 } }, true);
        check(tracker.Observe(account, state with { Jobs = state.Jobs! with { MasterLevel = 12 } }, true).Count == 0, "level sync cannot re-alert already observed master levels");
        check(tracker.Observe(account, state with { Jobs = state.Jobs! with { MainJob = "WHM", MasterLevel = 20 } }, true).Count == 0, "job switch establishes its own master-level baseline");
        check(tracker.Observe(account, state with { Nonce = "newsession0123456", Jobs = state.Jobs! with { MasterLevel = 30 } }, true).Count == 0, "new session does not infer historical level gains");
        check(tracker.RecoveryFailed(account.Id) != null && tracker.RecoveryFailed(account.Id) == null, "recovery failures deduplicate until recovery");
        tracker.Recovered(account.Id);
        check(tracker.RecoveryFailed(account.Id) != null, "verified recovery rearms failure alert");
        var window = new MaintenanceWindow("fixture", "Maintenance", now.AddHours(2), now.AddHours(3), "Odin");
        var profile = NotificationProfile.Default with { MaintenanceHours = 1 };
        check(tracker.Maintenance(account, profile, new([window], now), now).Count == 0, "maintenance outside configured threshold remains quiet");
        check(tracker.Maintenance(account, profile with { MaintenanceHours = 2 }, new([window], now), now).Count == 1, "maintenance alerts at inclusive threshold");
        check(tracker.Maintenance(account, profile, new([window], now), now.AddHours(1)).Count == 0, "stale maintenance feed cannot alert");
        check(tracker.Maintenance(account, profile with { MaintenanceHours = 2 }, new([window], now), now).Count == 0, "same maintenance notice alerts once");
        check(new CharacterAlertTracker().Maintenance(account with { World = "Asura" }, profile with { MaintenanceHours = 2 }, new([window], now), now).Count == 0, "maintenance filters other worlds");
        check(new CharacterAlertTracker().Maintenance(account, profile with { MaintenanceHours = 2 }, new([window], now, ["unverified"]), now).Count == 0, "unverified maintenance cannot alert");

        var directory = Path.Combine(Path.GetTempPath(), "ffxi-alert-tests-" + Guid.NewGuid());
        try
        {
            var store = new ConfigurationStore(directory);
            var custom = new NotificationProfile(Guid.NewGuid(), "Farming", [AlertEvent.MeritsCapped], 6);
            store.Save(new(Accounts: [account with { NotificationsEnabled = true, NotificationProfileId = custom.Id }])
            { Notifications = new() { Profiles = [custom], TelegramChatId = "1234" } });
            var loaded = store.Load();
            check(loaded.Notifications.Profile(loaded.ConfiguredAccounts.Single())?.Events.SequenceEqual(custom.Events) == true && loaded.ConfiguredAccounts.Single().NotificationsEnabled,
                "notification profile and per-character selection survive reload");
            check(!File.ReadAllText(Path.Combine(directory, "accounts.json")).Contains("BotToken"), "bot token is excluded from plain configuration");
            var stem = "odin_tester";
            var prefix = $"{state.Nonce}\nTester\nOdin\n{now.AddMinutes(-1).ToUnixTimeSeconds()}\n{now.ToUnixTimeSeconds()}\n";
            File.WriteAllText(Path.Combine(directory, stem + ".state"), prefix + "100\n100\n0\n0\n0\n100\nField\n0\n");
            var pointsPath = Path.Combine(directory, stem + ".points");
            File.WriteAllText(pointsPath, prefix + "123\n56000\n75\n75\n29999\n500\n");
            var reader = new CharacterStateReader(directory);
            check(reader.Read(account, now)?.Points is { Merits: 75, JobPoints: 500 }, "progression sidecar reads actual values and ceilings");
            check(reader.Read(account, now)?.Points is { Exemplar: null, ExemplarRequired: null }, "legacy points remain readable without master EXP");
            File.WriteAllText(pointsPath, prefix + "123\n56000\n75\n75\n29999\n500\n123456\n1500000\n");
            var exemplar = reader.Read(account, now)!.Points!;
            check(exemplar is { Exemplar: 123456, ExemplarRequired: 1500000 } && exemplar.Describe(45).Contains("123,456/1,500,000 Exemplar Points → ML 46"), "master EXP displays current progress and the next master-level requirement");
            check(exemplar.Describe(50).Contains("Master level 50 · capped") && !exemplar.Describe(50).Contains("→ ML 51"), "master-level cap never invents a level 51 target");
            check(!exemplar.Describe(0, false).Contains("Exemplar"), "jobs below 99 do not display unavailable master-level progress");
            File.WriteAllText(pointsPath, prefix.Replace(state.Nonce, "ffffffffffffffff") + "123\n56000\n75\n75\n29999\n500\n");
            check(reader.Read(account, now) is { Points: null, Hp: 100 }, "mismatched stats session cannot leak points or suppress vitals");
            File.WriteAllText(pointsPath, prefix + "-1\n-1\n-1\n-1\n-1\n-1\n");
            check(reader.Read(account, now)?.Points is { Experience: null, Merits: null }, "unavailable progression stays unknown instead of zero");
            File.WriteAllText(pointsPath, prefix + "123\n56000\n76\n75\n29999\n500\n");
            check(reader.Read(account, now) is { Points: null }, "invalid progression is rejected independently of other stats");
        }
        finally { Directory.Delete(directory, true); }

        var token = "123456:" + new string('x', 32);
        using var handler = new TelegramHandler();
        using var client = new HttpClient(handler);
        new TelegramNotificationSender(client).SendAsync(token, "-1234", "Tester died", CancellationToken.None).GetAwaiter().GetResult();
        check(handler.Chat == "-1234" && handler.Text == "Tester died", "Telegram uses structured chat ID and plain message body");
        var chats = new TelegramNotificationSender(client).FindChatsAsync(token, CancellationToken.None).GetAwaiter().GetResult();
        check(chats.Single() is { Id: "-1001234", Name: "Farming group" }, "chat discovery preserves negative group IDs and deduplicates updates");
        handler.Fail = true;
        try { new TelegramNotificationSender(client).SendAsync(token, "1234", "test", CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("Expected delivery failure"); }
        catch (InvalidOperationException ex) { check(!ex.ToString().Contains(token), "transport failures never expose bot tokens"); }
        try { TelegramNotificationSender.Validate("bad-token", "1234"); throw new Exception("Invalid token accepted"); }
        catch (ArgumentException) { check(true, "invalid bot token is rejected before network request"); }
    }

    private sealed class TelegramHandler : HttpMessageHandler
    {
        public bool Fail;
        public string? Chat;
        public string? Text;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Fail) throw new HttpRequestException("secret URL " + request.RequestUri);
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true,\"result\":[{\"message\":{\"chat\":{\"id\":-1001234,\"title\":\"Farming group\"}}},{\"message\":{\"chat\":{\"id\":-1001234,\"title\":\"Farming group\"}}}]}") };
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Chat = body.RootElement.GetProperty("chat_id").GetString();
            Text = body.RootElement.GetProperty("text").GetString();
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") };
        }
    }
}
