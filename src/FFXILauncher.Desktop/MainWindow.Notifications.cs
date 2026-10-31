using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using FFXILauncher.Core;
using FFXILauncher.Desktop.Platform;

namespace FFXILauncher.Desktop;

public sealed partial class MainWindow
{
    private static readonly Guid TelegramSecretId = Guid.Parse("e19909ce-de07-4b33-b978-9e8a5334a142");
    private readonly CharacterAlertTracker alertTracker = new();
    private readonly HttpClient telegramClient = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
    private readonly Queue<(Guid Account, Guid Profile, CharacterAlert Alert, DateTimeOffset Created, int Attempts)> notificationQueue = new();
    private readonly HashSet<string> gameMaintenanceNotified = [];
    private ICredentialVault telegramVault = null!;
    private string telegramToken = "";
    private bool sendingNotifications;
    private NotificationProfile? editingProfile;
    private readonly Dictionary<AlertEvent, CheckBox> eventChoices = [];

    private void InitializeNotifications()
    {
        telegramVault = new WindowsCredentialVault(Path.Combine(dataRoot, "notification-vault"));
        try { if (telegramVault.IsAvailable) telegramToken = telegramVault.Read(TelegramSecretId)?.PolPassword ?? ""; }
        catch (Exception) { NotificationStatus.Text = "Saved Telegram token could not be read. Enter it again and save."; }
        TelegramEnabled.IsChecked = configuration.Notifications.TelegramEnabled;
        TelegramChatId.Text = configuration.Notifications.TelegramChatId;
        TelegramToken.PlaceholderText = telegramToken.Length > 0 ? "Token saved · leave blank to keep" : "BotFather token";
        NotificationPanel.IsEnabled = configurationReadable;
        foreach (var kind in Enum.GetValues<AlertEvent>())
        {
            var choice = new CheckBox { Content = AlertLabel(kind), Margin = new Thickness(0, 0, 16, 0) };
            eventChoices[kind] = choice;
            NotificationEventChoices.Children.Add(choice);
        }
        RefreshNotificationProfiles();
    }

    private static string AlertLabel(AlertEvent kind) => kind switch
    {
        AlertEvent.Maintenance => "Maintenance / in-game warnings",
        AlertEvent.Death => "Character death",
        AlertEvent.RecoveryFailed => "Dropped session: recovery failed",
        AlertEvent.MeritsCapped => "Merits capped",
        AlertEvent.CapacityCapped => "Capacity capped",
        AlertEvent.JobPointsCapped => "Job points capped",
        _ => "Every master level earned"
    };

    private void RefreshNotificationProfiles(Guid? selected = null)
    {
        NotificationProfilePicker.ItemsSource = configuration.Notifications.Profiles;
        NotificationProfilePicker.SelectedItem = configuration.Notifications.Profiles.FirstOrDefault(p => p.Id == selected)
            ?? configuration.Notifications.Profiles.FirstOrDefault();
        LoadNotificationProfile();
    }

    private void NotificationProfileChanged(object? sender, SelectionChangedEventArgs e) => LoadNotificationProfile();
    private void LoadNotificationProfile()
    {
        editingProfile = NotificationProfilePicker.SelectedItem as NotificationProfile;
        if (editingProfile == null) return;
        NotificationProfileName.Text = editingProfile.Name;
        NotificationMaintenanceHours.Value = Math.Clamp(editingProfile.MaintenanceHours, 0, 168);
        foreach (var (kind, choice) in eventChoices) choice.IsChecked = editingProfile.Events.Contains(kind);
    }

    private void SaveNotificationProfile(object? sender, RoutedEventArgs e)
    {
        if (editingProfile == null) return;
        var name = NotificationProfileName.Text?.Trim() ?? "";
        if (name.Length is < 1 or > 80) { NotificationStatus.Text = "Use a profile name between 1 and 80 characters."; return; }
        var profile = editingProfile with { Name = name, Events = eventChoices.Where(c => c.Value.IsChecked == true).Select(c => c.Key).ToList(),
            MaintenanceHours = (int)Math.Clamp(decimal.Round(NotificationMaintenanceHours.Value ?? 12), 0, 168) };
        if (SaveNotificationSettings(configuration.Notifications with { Profiles = configuration.Notifications.Profiles.Select(p => p.Id == profile.Id ? profile : p).ToList() }))
        { RefreshNotificationProfiles(profile.Id); RenderAccounts(); NotificationStatus.Text = "Alert profile saved."; }
    }

    private void AddNotificationProfile(object? sender, RoutedEventArgs e)
    {
        var profile = new NotificationProfile(Guid.NewGuid(), "Custom alerts", [AlertEvent.Death, AlertEvent.RecoveryFailed]);
        if (SaveNotificationSettings(configuration.Notifications with { Profiles = [.. configuration.Notifications.Profiles, profile] }))
        { RefreshNotificationProfiles(profile.Id); RenderAccounts(); }
    }

    private void DeleteNotificationProfile(object? sender, RoutedEventArgs e)
    {
        if (editingProfile == null) return;
        if (editingProfile.Id == NotificationProfile.DefaultId) { NotificationStatus.Text = "The default profile is used for new characters. You can edit its events or create a custom profile."; return; }
        if (configuration.Notifications.Profiles.Count <= 1) { NotificationStatus.Text = "Keep at least one alert profile."; return; }
        var id = editingProfile.Id;
        if (configuration.ConfiguredAccounts.Any(a => a.NotificationProfileId == id))
        { NotificationStatus.Text = "Choose another profile on characters using this one before deleting it."; return; }
        if (SaveNotificationSettings(configuration.Notifications with { Profiles = configuration.Notifications.Profiles.Where(p => p.Id != id).ToList() }))
        { RefreshNotificationProfiles(); RenderAccounts(); }
    }

    private bool SaveNotificationSettings(NotificationSettings settings)
    {
        try
        {
            if (!configurationReadable) throw new InvalidOperationException("Configuration could not be loaded.");
            var next = configuration with { Notifications = settings };
            store.Save(next); configuration = next;
            return true;
        }
        catch (Exception) { NotificationStatus.Text = "Could not save notification settings. Check access to the launcher data folder."; return false; }
    }

    private void SaveTelegram(object? sender, RoutedEventArgs e)
    {
        try
        {
            var replacement = TelegramToken.Text?.Trim() ?? "";
            var token = replacement.Length == 0 ? telegramToken : replacement;
            var chat = TelegramChatId.Text?.Trim() ?? "";
            var enabled = TelegramEnabled.IsChecked == true;
            if (enabled || replacement.Length > 0) TelegramNotificationSender.Validate(token, chat);
            if (replacement.Length > 0)
            {
                telegramVault.Save(TelegramSecretId, new AccountCredentials(replacement, ""));
                telegramToken = replacement;
                TelegramToken.Text = "";
                TelegramToken.PlaceholderText = "Token saved · leave blank to keep";
            }
            if (SaveNotificationSettings(configuration.Notifications with { TelegramEnabled = enabled, TelegramChatId = chat }))
            {
                notificationQueue.Clear();
                NotificationStatus.Text = enabled ? "Telegram saved. Enable notifications on each character you want to follow." : "Telegram notifications disabled.";
            }
        }
        catch (ArgumentException ex) { NotificationStatus.Text = ex.Message; }
        catch (Exception) { NotificationStatus.Text = "Could not save the encrypted bot token. Token storage requires Windows."; }
    }

    private void ClearTelegram(object? sender, RoutedEventArgs e)
    {
        if (!SaveNotificationSettings(configuration.Notifications with { TelegramEnabled = false })) return;
        try
        {
            if (telegramVault.Contains(TelegramSecretId)) telegramVault.Delete(TelegramSecretId);
            telegramToken = ""; TelegramToken.Text = "";
            TelegramToken.PlaceholderText = "BotFather token"; TelegramEnabled.IsChecked = false;
            notificationQueue.Clear(); NotificationStatus.Text = "Telegram disabled and saved token removed.";
        }
        catch (Exception) { NotificationStatus.Text = "Telegram disabled; saved token could not be removed."; }
    }

    private async void FindTelegramChat(object? sender, RoutedEventArgs e)
    {
        if (sendingNotifications) return;
        sendingNotifications = true;
        try
        {
            var token = string.IsNullOrWhiteSpace(TelegramToken.Text) ? telegramToken : TelegramToken.Text.Trim();
            var chats = await new TelegramNotificationSender(telegramClient).FindChatsAsync(token, shutdown.Token);
            TelegramChatPicker.ItemsSource = chats;
            TelegramChatPicker.IsVisible = chats.Count > 0;
            if (chats.Count == 1) TelegramChatPicker.SelectedIndex = 0;
            NotificationStatus.Text = chats.Count == 0 ? "No recent chats. Send /start to the bot, then find chats again." : "Select your chat, then save Telegram and send a test.";
        }
        catch (OperationCanceledException) { NotificationStatus.Text = "Chat lookup timed out or was cancelled."; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { NotificationStatus.Text = ex.Message; }
        catch (Exception) { NotificationStatus.Text = "Chat lookup failed. Enter the chat ID manually."; }
        finally { sendingNotifications = false; }
    }

    private void TelegramChatChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TelegramChatPicker.SelectedItem is TelegramChat chat) TelegramChatId.Text = chat.Id;
    }

    private async void TestTelegram(object? sender, RoutedEventArgs e)
    {
        if (sendingNotifications) return;
        sendingNotifications = true;
        try
        {
            var token = string.IsNullOrWhiteSpace(TelegramToken.Text) ? telegramToken : TelegramToken.Text.Trim();
            await new TelegramNotificationSender(telegramClient).SendAsync(token, TelegramChatId.Text?.Trim() ?? "",
                "FFXI Account Manager · Telegram test notification. Your bot is connected.", shutdown.Token);
            NotificationStatus.Text = "Test notification sent.";
        }
        catch (OperationCanceledException) { NotificationStatus.Text = "Telegram test timed out or was cancelled."; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { NotificationStatus.Text = ex.Message; }
        catch (Exception) { NotificationStatus.Text = "Telegram test failed. Check the bot token, chat ID and connection."; }
        finally { sendingNotifications = false; }
    }

    private void AddCharacterNotificationControls(Panel settings, AccountConfiguration account)
    {
        var toggle = new ToggleSwitch { Name = "Notifications_" + account.Id.ToString("N"), Content = "Notifications", IsChecked = account.NotificationsEnabled, Margin = new Thickness(0, 0, 12, 0) };
        var picker = new ComboBox { Name = "NotificationProfile_" + account.Id.ToString("N"), ItemsSource = configuration.Notifications.Profiles,
            SelectedItem = configuration.Notifications.Profile(account), MinWidth = 180, MaxWidth = 300,
            IsVisible = configuration.Notifications.Profiles.Any(p => p.Id != NotificationProfile.DefaultId) };
        ToolTip.SetTip(picker, "Alert profile · configure events in Notifications below maintenance");
        settings.Children.Add(toggle); settings.Children.Add(picker);
        var updating = false;
        void Update()
        {
            if (updating) return;
            updating = true;
            try
            {
                if (!configurationReadable) throw new InvalidOperationException();
                var next = configuration with { Accounts = configuration.ConfiguredAccounts.Select(a => a.Id == account.Id
                    ? a with { NotificationsEnabled = toggle.IsChecked == true, NotificationProfileId = (picker.SelectedItem as NotificationProfile)?.Id ?? a.NotificationProfileId } : a).ToList() };
                store.Save(next); configuration = next;
            }
            catch (Exception)
            {
                var saved = configuration.ConfiguredAccounts.Single(a => a.Id == account.Id);
                toggle.IsChecked = saved.NotificationsEnabled; picker.SelectedItem = configuration.Notifications.Profile(saved);
                Log("Could not save character notification settings.");
            }
            finally { updating = false; }
        }
        toggle.IsCheckedChanged += (_, _) => Update(); picker.SelectionChanged += (_, _) => Update();
    }

    private bool CanNotify(AccountConfiguration account, AlertEvent kind) => configuration.Notifications.TelegramEnabled &&
        telegramToken.Length > 0 && account.NotificationsEnabled && configuration.Notifications.Profile(account)?.Events.Contains(kind) == true;

    private void QueueNotification(AccountConfiguration account, CharacterAlert alert)
    {
        if (!CanNotify(account, alert.Event)) return;
        if (notificationQueue.Count >= 100) { NotificationStatus.Text = "Notification queue full; additional alerts were skipped."; return; }
        notificationQueue.Enqueue((account.Id, account.NotificationProfileId, alert, DateTimeOffset.UtcNow, 0));
    }

    private void NotifyRecoveryFailure(AccountConfiguration account)
    {
        if (alertTracker.RecoveryFailed(account.Id) is {} alert) QueueNotification(account, alert);
    }

    private void CheckMaintenanceNotifications()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var account in configuration.ConfiguredAccounts)
        {
            if (!CanNotify(account, AlertEvent.Maintenance) || configuration.Notifications.Profile(account) is not {} profile) continue;
            foreach (var alert in alertTracker.Maintenance(account, profile, maintenance.Snapshot, now)) QueueNotification(account, alert);
            foreach (var warning in GameAlerts(account))
            {
                if (InGameMaintenance.Completed(warning, maintenance.Snapshot, now)) continue;
                var key = $"{account.Id}:{warning.ObservedUtc}:{warning.Message}";
                if (gameMaintenanceNotified.Add(key)) QueueNotification(account, new(AlertEvent.Maintenance, key, "In-game maintenance warning: " + warning.Message));
            }
        }
    }

    private async Task FlushNotifications()
    {
        if (sendingNotifications || notificationQueue.Count == 0 || shutdown.IsCancellationRequested) return;
        sendingNotifications = true;
        try
        {
            var item = notificationQueue.Dequeue();
            var account = configuration.ConfiguredAccounts.FirstOrDefault(a => a.Id == item.Account);
            if (account == null || account.NotificationProfileId != item.Profile || !CanNotify(account, item.Alert.Event) ||
                DateTimeOffset.UtcNow - item.Created > TimeSpan.FromMinutes(10)) return;
            try
            {
                await new TelegramNotificationSender(telegramClient).SendAsync(telegramToken, configuration.Notifications.TelegramChatId,
                    $"FFXI Account Manager · {account.Character} ({account.World})\n{item.Alert.Message}", shutdown.Token);
                NotificationStatus.Text = $"Last sent: {account.Character} · {AlertLabel(item.Alert.Event)} · {DateTimeOffset.Now:t}";
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
            catch (Exception)
            {
                if (item.Attempts < 2) notificationQueue.Enqueue(item with { Attempts = item.Attempts + 1 });
                NotificationStatus.Text = item.Attempts < 2 ? "Telegram delivery failed; retry queued." : "Telegram delivery failed after 3 attempts. Check settings and use Send test.";
            }
        }
        finally { sendingNotifications = false; }
    }
}
