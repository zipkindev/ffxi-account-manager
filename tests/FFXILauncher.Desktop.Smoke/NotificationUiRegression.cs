using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FFXILauncher.Core;
using FFXILauncher.Desktop;

internal static class NotificationUiRegression
{
    internal static void Run(MainWindow window, string directory, Guid accountId)
    {
        var store = new ConfigurationStore(directory);
        var panel = window.FindControl<Border>("NotificationPanel")!;
        var maintenance = window.FindControl<Border>("MainMaintenancePanel")!;
        if (panel.Parent is not StackPanel stack || stack.Children.IndexOf(panel) != stack.Children.IndexOf(maintenance) + 1)
            throw new Exception("Notification settings must appear directly below maintenance.");
        if (window.FindControl<ToggleSwitch>("TelegramEnabled")!.IsChecked == true || window.FindControl<TextBox>("TelegramToken")!.PasswordChar == default)
            throw new Exception("Telegram must be optional and bot token entry masked.");
        void Click(string text) => window.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, text)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var initialToggle = window.GetLogicalDescendants().OfType<ToggleSwitch>().Single(t => t.Name == "Notifications_" + accountId.ToString("N"));
        var initialPicker = window.GetLogicalDescendants().OfType<ComboBox>().Single(t => t.Name == "NotificationProfile_" + accountId.ToString("N"));
        if (initialPicker.IsVisible || initialToggle.Parent is not WrapPanel policies ||
            !policies.Children.OfType<ToggleSwitch>().Any(t => Equals(t.Content, "Auto login")) ||
            !policies.Children.OfType<ToggleSwitch>().Any(t => Equals(t.Content, "Maintain connection")) ||
            !policies.Children.OfType<ToggleSwitch>().Any(t => Equals(t.Content, "Schedule server maintenance")))
            throw new Exception("Default notification control must be a simple toggle alongside the other character policies.");
        Click("New profile");
        window.FindControl<TextBox>("NotificationProfileName")!.Text = "Farming alerts";
        window.FindControl<NumericUpDown>("NotificationMaintenanceHours")!.Value = 4;
        Click("Save alert profile");
        var custom = store.Load().Notifications.Profiles.Single(p => p.Name == "Farming alerts");
        if (custom.MaintenanceHours != 4 || !custom.Events.Contains(AlertEvent.Death)) throw new Exception("Custom notification profile did not persist.");
        var toggle = window.GetLogicalDescendants().OfType<ToggleSwitch>().Single(t => t.Name == "Notifications_" + accountId.ToString("N"));
        var picker = window.GetLogicalDescendants().OfType<ComboBox>().Single(t => t.Name == "NotificationProfile_" + accountId.ToString("N"));
        if (!picker.IsVisible) throw new Exception("Creating a custom profile must reveal the optional per-character profile selector.");
        toggle.IsChecked = true;
        picker.SelectedItem = picker.Items.OfType<NotificationProfile>().Single(p => p.Id == custom.Id);
        var saved = store.Load().ConfiguredAccounts.Single(a => a.Id == accountId);
        if (!saved.NotificationsEnabled || saved.NotificationProfileId != custom.Id || store.Load().Notifications.TelegramEnabled)
            throw new Exception("Character notification policy must save independently of delivery and automation policy.");
        Click("Delete profile");
        if (!store.Load().Notifications.Profiles.Any(p => p.Id == custom.Id)) throw new Exception("Deleting an assigned profile must be blocked.");
        var editor = new AccountDialog(saved, false, false);
        var result = editor.ShowDialog<AccountEdit?>(window);
        Dispatcher.UIThread.RunJobs();
        editor.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "SaveAccountButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        if (result.GetAwaiter().GetResult()?.Account is not { NotificationsEnabled: true } edited || edited.NotificationProfileId != custom.Id)
            throw new Exception("Editing an account must preserve notification preferences.");
        Click("Remove saved token");
        if (store.Load().Notifications.TelegramEnabled || window.FindControl<TextBlock>("NotificationStatus")!.Text != "Telegram disabled and saved token removed.")
            throw new Exception("Removing Telegram token must disable delivery even when no token was previously saved.");
        var account = store.Load().ConfiguredAccounts.Single(a => a.Id == accountId);
        var data = Path.Combine(Path.GetDirectoryName(store.Load().WindowerPath)!, "addons", "FFXIManager", "data");
        var stem = account.World.ToLowerInvariant() + "_" + account.Character.ToLowerInvariant();
        var prefix = File.ReadAllLines(Path.Combine(data, stem + ".state")).Take(5);
        File.WriteAllLines(Path.Combine(data, stem + ".points"), prefix.Concat(new[] { "12345", "56000", "75", "75", "29999", "500", "123456", "1500000" }));
        typeof(MainWindow).GetMethod("RefreshSessions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, null);
        var stats = window.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "CharacterState_" + accountId.ToString("N")).Text!;
        if (!stats.Contains("12,345/56,000 EXP") || !stats.Contains("75/75 Merits") || !stats.Contains("29,999/30,000 Capacity") || !stats.Contains("500/500 Job Points") || !stats.Contains("123,456/1,500,000 Exemplar Points"))
            throw new Exception("Character cards must display each point counter with its own requirement or cap.");
        panel.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        using (var frame = window.CaptureRenderedFrame() ?? throw new Exception("No notification frame"))
            frame.Save("artifacts/notifications.png", PngBitmapEncoderOptions.Default);
        Console.WriteLine("PASS: notification settings placement, opt-in defaults, custom profiles, per-character policy, account editing and assigned-profile deletion guards.");
    }
}
