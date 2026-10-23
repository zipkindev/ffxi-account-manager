using System.Net;
using System.Text.Json;
using FFXILauncher.Core;
using FFXILauncher.Desktop.Platform;

internal static class MaintenanceMonitorRegression
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ffxi-maintenance-regression-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var checkedAt = DateTimeOffset.UtcNow.AddHours(-1);
            var window = new MaintenanceWindow("https://www.playonline.com/ff11us/polnews/news100.shtml",
                "Cached maintenance", DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow.AddHours(2));
            var original = JsonSerializer.Serialize(new MaintenanceSnapshot([window], checkedAt));
            var cachePath = Path.Combine(directory, "maintenance.json");
            File.WriteAllText(cachePath, original);
            using var handler = new NoticeHandler();
            using var monitor = new MaintenanceMonitor(directory, handler);
            monitor.Load();
            var cached = monitor.Snapshot;

            // The real endpoint redirects to a placeholder which still returns 200.
            monitor.RefreshAsync(true, CancellationToken.None).GetAwaiter().GetResult();
            AssertUnavailable();
            var requests = handler.Requests;
            monitor.RefreshAsync(false, CancellationToken.None).GetAwaiter().GetResult();
            if (handler.Requests != requests) throw new Exception("Unavailable feeds must retain the polling backoff.");

            handler.Failure = new HttpRequestException("Fixture network failure");
            monitor.RefreshAsync(true, CancellationToken.None).GetAwaiter().GetResult();
            AssertUnavailable();

            handler.Failure = null;
            handler.Healthy = true;
            monitor.RefreshAsync(true, CancellationToken.None).GetAwaiter().GetResult();
            if (monitor.Snapshot.CheckedUtc <= checkedAt || monitor.Snapshot.Windows.Count != 2 ||
                !monitor.Status.StartsWith("Official notices checked"))
                throw new Exception("Notice checking did not recover after an unavailable feed.");
            if (JsonSerializer.Deserialize<MaintenanceSnapshot>(File.ReadAllText(cachePath))?.Windows.Count != 2)
                throw new Exception("Recovered notice data was not persisted.");
            handler.SupportOnly = true;
            var beforeSupport = handler.Requests;
            monitor.RefreshAsync(true, CancellationToken.None).GetAwaiter().GetResult();
            if (handler.Requests != beforeSupport + 1 || monitor.Snapshot.ReviewRequired?.Count != 0)
                throw new Exception("Support-center-only notices must not block game reconnection or require notice parsing.");

            void AssertUnavailable()
            {
                if (!ReferenceEquals(monitor.Snapshot, cached) || monitor.Snapshot.CheckedUtc != checkedAt ||
                    File.ReadAllText(cachePath) != original || !monitor.Status.StartsWith("Official notices unavailable"))
                    throw new Exception("An unavailable notice feed must preserve cached schedules and freshness without throwing.");
                var account = new AccountConfiguration(Guid.NewGuid(), "Fixture", "Default", "Fixture", "Asura", "", "FixtureID") with { MaintainConnection = true };
                if (RecoveryPolicy.Evaluate(account, false, monitor.Snapshot, DateTimeOffset.UtcNow).Allowed)
                    throw new Exception("An unavailable feed must not turn a stale check into reconnect authorization.");
            }
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("PASS: maintenance placeholder/network failure preserves cache, freshness and backoff; later refresh recovers.");
    }

    private sealed class NoticeHandler : HttpMessageHandler
    {
        public bool Healthy { get; set; }
        public bool SupportOnly { get; set; }
        public Exception? Failure { get; set; }
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (Failure != null) return Task.FromException<HttpResponseMessage>(Failure);
            var body = !Healthy ? "<html><h2>Maintenance</h2>The service is currently unavailable due to maintenance.</html>"
                : request.RequestUri!.AbsolutePath.EndsWith("list_mnt.shtml")
                    ? SupportOnly ? "<a href='../polnews/news200.shtml'>Support Center Emergency Maintenance (Oct. 21)</a>" : "<a href='../polnews/news101.shtml'>All Worlds Maintenance</a>"
                    : "FINAL FANTASY XI [Date &amp; Time] Oct. 22, 2099 1:00 to 2:00 (UTC) [Affected Worlds] All Worlds";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
