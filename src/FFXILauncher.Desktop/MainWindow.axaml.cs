using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FFXILauncher.Core;
using FFXILauncher.Desktop.Platform;

namespace FFXILauncher.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly ConfigurationStore store;
    private readonly string dataRoot;
    private readonly ICredentialVault vault;
    private readonly ISessionPlatform platform;
    private readonly IAutoLoginAutomation autoLogin;
    private readonly WindowsCalibrationControl? calibrationControl;
    private readonly MaintenanceMonitor maintenance;
    private readonly HashSet<Guid> intentionallyStopped = [];
    private readonly HashSet<Guid> shuttingDown = [];
    private readonly HashSet<Guid> forceClosing = [];
    private readonly Dictionary<Guid, string> shutdownProgress = [];
    private readonly Dictionary<Guid, CancellationTokenSource> shutdownStops = [];
    private readonly Dictionary<Guid, GameSession> shutdownSessions = [];
    private readonly Dictionary<Guid, TaskCompletionSource> shutdownFinished = [];
    private readonly HashSet<Guid> shutdownCancelling = [];
    private readonly HashSet<Guid> forcedClosed = [];
    private readonly Dictionary<Guid, CancellationTokenSource> accountCancellation = [];
    private readonly Dictionary<Guid, DateTimeOffset> nextRetry = [];
    private bool recoveryChecking;
    private readonly HashSet<string> scheduledShutdowns = [];
    private LauncherConfiguration configuration = new();
    private readonly Dictionary<Guid, ManagedProcess> sessions = [];
    private readonly Dictionary<Guid, TextBlock> statuses = [];
    private readonly Dictionary<Guid, TextBlock> characterMaintenance = [];
    private readonly Dictionary<Guid, TextBlock> characterStates = [];
    private readonly Dictionary<Guid, StackPanel> characterWeather = [];
    private readonly HashSet<Guid> pending = [];
    private readonly HashSet<Guid> uncertain = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly CancellationTokenSource shutdown = new();
    private bool configurationReadable = true;
    private bool batchLaunching;
    private bool addonDeploying;
    private readonly HashSet<Guid> selectedAccounts = [];
    private bool updatingSelection;
    private bool updatingTheme;
    private Bitmap? themeArtwork;
    private readonly Dictionary<string, Bitmap> landscapeArtwork = [];

    public MainWindow() : this(null) { }
    public MainWindow(string? dataDirectory, bool discoverOnStartup = true)
    {
        platform = new WindowsSessionPlatform();
        InitializeComponent();
        UnattendedSetupButton.IsVisible = OperatingSystem.IsWindows();
        VersionButton.Content = "v" + AppRelease.Version + " · What’s new";
        if (WindowsUnattendedSetup.IsElevated) UnattendedStatus.Text = "Administrator access active. Launch, login and process cleanup can run without additional UAC prompts.";
        var directory = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FFXILauncher");
        dataRoot = directory;
        autoLogin = new WindowsAutoLogin(directory);
        if (discoverOnStartup && WindowsUnattendedSetup.IsElevated && dataDirectory == null)
            calibrationControl = new WindowsCalibrationControl(directory, async (request, cancellation) =>
                await Dispatcher.UIThread.InvokeAsync(() => HandleCalibration(request, cancellation)));
        if (autoLogin is WindowsAutoLogin windowsLogin)
            windowsLogin.Progress += (id, message) => { calibrationControl?.Report(message); Dispatcher.UIThread.Post(() => { if (statuses.TryGetValue(id, out var status)) status.Text = message; }); };
        store = new(directory);
        maintenance = new MaintenanceMonitor(directory);
        maintenance.Load();
        vault = new WindowsCredentialVault(Path.Combine(directory, "vault"));
        try { configuration = store.Load(); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            configurationReadable = false;
            Activity.Text = "Configuration could not be loaded. Preserve accounts.json and repair it before saving: " + ex.Message;
            LaunchEnabledButton.IsEnabled = false;
        }
        updatingTheme = true;
        ThemePicker.ItemsSource = ThemeCatalog.All;
        ThemePicker.SelectedItem = ThemeCatalog.Resolve(configuration.Theme);
        ThemePicker.IsEnabled = configurationReadable;
        LandscapeCardsToggle.IsChecked = configuration.LandscapeCards;
        ApplyTheme(ThemeCatalog.Resolve(configuration.Theme));
        updatingTheme = false;
        WindowerPath.Text = configuration.WindowerPath;
        PolPath.Text = configuration.PolPath;
        if (!platform.CanLaunch) Footer.Text = "UI preview on Linux. Launching and credential storage are Windows-only in this milestone.";
        LaunchEnabledButton.IsEnabled = platform.CanLaunch && configurationReadable;
        StartEnabledButton.IsEnabled = CanAutoLogin;
        if (discoverOnStartup) DetectInstallationPaths(replaceExisting: false);
        if (discoverOnStartup) RestoreManagedSessions();
        RenderAccounts();
        if (discoverOnStartup && dataDirectory == null && WindowsUnattendedSetup.IsElevated)
            Opened += (_, _) => UpdateExistingControlAddon();
        if (discoverOnStartup) DiscoverSessions(null, new RoutedEventArgs());
        timer.Tick += async (_, _) => { AttachVerifiedSessions(); CollectCharacterWorlds(); RefreshSessions(); await CheckRecovery(); };
        timer.Start();
        SizeChanged += (_, _) => UpdateArtworkLayout();
        Closed += (_, _) => { shutdown.Cancel(); timer.Stop(); calibrationControl?.Dispose(); (autoLogin as IDisposable)?.Dispose(); themeArtwork?.Dispose(); foreach (var bitmap in landscapeArtwork.Values) bitmap.Dispose(); };
    }
    private async void ShowReleaseNotes(object? sender, RoutedEventArgs args) =>
        await ShowDocument("FFXI Account Manager · v" + AppRelease.Version, AppRelease.Notes);

    private async void ShowLicenses(object? sender, RoutedEventArgs args) =>
        await ShowDocument("Licenses & credits", AppRelease.Licenses);

    private async Task ShowDocument(string title, string document)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 700, Height = 560, MinWidth = 440, MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = ThemeCatalog.BrushFor("Background"),
            Foreground = ThemeCatalog.BrushFor("Text")
        };
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => dialog.Close();
        var layout = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(24) };
        layout.Children.Add(new ScrollViewer { Content = new SelectableTextBlock { Text = document, TextWrapping = TextWrapping.Wrap } });
        Grid.SetRow(close, 1);
        layout.Children.Add(close);
        dialog.Content = layout;
        await dialog.ShowDialog(this);
    }
    private void Log(string message) => Activity.Text = DateTime.Now.ToString("HH:mm:ss") + "  " + message + "\n" + string.Join('\n', (Activity.Text ?? "").Split('\n').Take(9));
    private void RememberSession(Guid id, ManagedProcess process)
    {
        sessions[id] = process;
        if (string.IsNullOrWhiteSpace(configuration.PolPath)) return;
        try
        {
            new ManagedSessionStore(dataRoot).Save(sessions.Where(s => configuration.ConfiguredAccounts.Any(a => a.Id == s.Key) && platform.Inspect(s.Value) != SessionHealth.Exited)
                .Select(s => new ManagedSession(s.Key, s.Value, Path.GetFullPath(configuration.PolPath))));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        { Log("Session is tracked, but restart recovery could not be saved: " + ex.Message); }
    }
    private void RestoreManagedSessions()
    {
        if (!configurationReadable || platform is not WindowsSessionPlatform windows || !windows.CanLaunch || string.IsNullOrWhiteSpace(configuration.PolPath)) return;
        try
        {
            var running = windows.Discover();
            foreach (var saved in new ManagedSessionStore(dataRoot).Load())
            {
                var account = configuration.ConfiguredAccounts.SingleOrDefault(a => a.Id == saved.AccountId);
                if (account == null || !saved.PolPath.Equals(Path.GetFullPath(configuration.PolPath), StringComparison.OrdinalIgnoreCase)) continue;
                var live = running.SingleOrDefault(p => saved.Matches(p.Process, p.ExecutablePath));
                if (live == null || live.Character != null && !live.Character.Equals(account.Character, StringComparison.OrdinalIgnoreCase)) continue;
                sessions[account.Id] = saved.Process;
                Log(account.Label + ": restored managed POL session after app restart (PID " + saved.Process.Pid + ").");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        { Log("Saved session recovery needs attention: " + ex.Message); }
    }
    private async void InstallUnattendedAccess(object? sender, RoutedEventArgs args)
    {
        UnattendedSetupButton.IsEnabled = false;
        UnattendedStatus.Text = "Installing protected app and unattended shortcut. Approve the one-time Windows setup prompt.";
        try
        {
            await WindowsUnattendedSetup.InstallAsync();
            UnattendedStatus.Text = "Installed. Close this manager and open the FFXI Account Manager desktop shortcut. Future starts need no UAC prompt. Games remain running.";
            Log("Unattended access installed; restart using the desktop shortcut.");
        }
        catch (Exception ex) { UnattendedStatus.Text = ex.Message; }
        finally { UnattendedSetupButton.IsEnabled = true; }
    }
    private void Save()
    {
        if (!configurationReadable) throw new InvalidOperationException("Configuration could not be loaded; saving is disabled.");
        store.Save(configuration);
    }
    private void ApplyTheme(AppTheme theme)
    {
        Bitmap? nextArtwork = null;
        if (theme.Artwork != null)
        {
            using var stream = AssetLoader.Open(new Uri("avares://FFXILauncher.Desktop/Assets/Themes/" + theme.Artwork));
            nextArtwork = new Bitmap(stream);
        }
        ThemeCatalog.Apply(theme);
        BackgroundArtwork.Source = nextArtwork;
        PortraitArtwork.Source = theme.Portrait ? nextArtwork : null;
        BackgroundArtwork.Opacity = theme.Portrait ? .25 : .55;
        themeArtwork?.Dispose();
        themeArtwork = nextArtwork;
        ArtworkShade.IsVisible = nextArtwork != null;
        ArtworkCredit.IsVisible = nextArtwork != null;
        ArtworkCredit.Text = theme.Credit ?? "Artwork © SQUARE ENIX CO., LTD. All Rights Reserved.";
        UpdateArtworkLayout();
    }
    private void UpdateArtworkLayout()
    {
        var portrait = ThemeCatalog.Resolve(configuration.Theme).Portrait && Bounds.Width >= 1050;
        PortraitFrame.IsVisible = portrait;
        MainContentGrid.Margin = new Thickness(portrait ? 256 : 32, 32, 32, 32);
    }
    private Bitmap LandscapeBitmap(string file)
    {
        if (landscapeArtwork.TryGetValue(file, out var cached)) return cached;
        using var stream = AssetLoader.Open(new Uri("avares://FFXILauncher.Desktop/Assets/Themes/" + file));
        return landscapeArtwork[file] = new Bitmap(stream);
    }
    private void LandscapeCardsChanged(object? sender, RoutedEventArgs args)
    {
        if (updatingTheme || !configurationReadable || LoginOperationActive) return;
        var previous = configuration;
        try
        {
            var next = configuration with { LandscapeCards = LandscapeCardsToggle.IsChecked == true };
            store.Save(next); configuration = next; RenderAccounts();
        }
        catch (Exception ex)
        {
            configuration = previous; updatingTheme = true;
            LandscapeCardsToggle.IsChecked = previous.LandscapeCards; updatingTheme = false;
            Log("Could not save card appearance: " + ex.Message);
        }
    }
    private void ShuffleArtwork(object? sender, RoutedEventArgs args)
    {
        if (!configurationReadable || LoginOperationActive) return;
        try
        {
            var choices = configuration.ConfiguredAccounts.ToDictionary(a => a.Id, a =>
            {
                var previous = configuration.CharacterArtwork?.GetValueOrDefault(a.Id) ?? ThemeCatalog.DefaultLandscape(a.Id);
                var candidates = ThemeCatalog.Landscapes.Where(file => file != previous).ToArray();
                return candidates[Random.Shared.Next(candidates.Length)];
            });
            var next = configuration with { CharacterArtwork = choices };
            store.Save(next); configuration = next; RenderAccounts();
            Log("New character landscapes saved.");
        }
        catch (Exception ex) { Log("Could not shuffle artwork: " + ex.Message); }
    }
    private void ThemeChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (updatingTheme || !configurationReadable || ThemePicker.SelectedItem is not AppTheme theme) return;
        var previous = configuration;
        try
        {
            if (LoginOperationActive) throw new InvalidOperationException("Wait for the current account operation to finish.");
            var next = configuration with { Theme = theme.Id };
            ApplyTheme(theme);
            store.Save(next);
            configuration = next;
            UpdateArtworkLayout();
            RenderAccounts();
        }
        catch (Exception ex)
        {
            configuration = previous;
            updatingTheme = true;
            ThemePicker.SelectedItem = ThemeCatalog.Resolve(previous.Theme);
            updatingTheme = false;
            ApplyTheme(ThemeCatalog.Resolve(previous.Theme));
            Log("Could not change theme: " + ex.Message);
        }
    }
    private async void CopyAddonCommand(object? sender, RoutedEventArgs args)
    {
        try
        {
            var clipboard = GetTopLevel(this)?.Clipboard ?? throw new InvalidOperationException("Clipboard unavailable.");
            await clipboard.SetTextAsync(InitialAddonCommand.Text ?? "//lua load FFXIManager");
            ToolTip.SetTip(CopyAddonCommandButton, "Copied to clipboard");
            Log("Addon command copied to clipboard.");
        }
        catch (Exception ex) { Log("Could not copy addon command: " + ex.Message); }
    }
    private void DetectPaths(object? sender, RoutedEventArgs args) => DetectInstallationPaths(replaceExisting: true);
    private void DetectInstallationPaths(bool replaceExisting)
    {
        if (!configurationReadable) return;
        try
        {
            var detection = WindowsInstallationDiscovery.Detect();
            var next = configuration with
            {
                WindowerPath = (replaceExisting || string.IsNullOrWhiteSpace(configuration.WindowerPath)) && detection.WindowerPath != null ? detection.WindowerPath : configuration.WindowerPath,
                PolPath = (replaceExisting || string.IsNullOrWhiteSpace(configuration.PolPath)) && detection.PolPath != null ? detection.PolPath : configuration.PolPath
            };
            if (next != configuration) { store.Save(next); configuration = next; }
            WindowerPath.Text = configuration.WindowerPath; PolPath.Text = configuration.PolPath;
            InstallationStatus.Text = detection.Status;
            LauncherSetup.IsExpanded = !File.Exists(configuration.WindowerPath) || !File.Exists(configuration.PolPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { InstallationStatus.Text = "Installation detection needs attention: " + ex.Message; }
    }
    private void SavePaths(object? sender, RoutedEventArgs args)
    {
        try { configuration = configuration with { WindowerPath = WindowerPath.Text?.Trim() ?? "", PolPath = PolPath.Text?.Trim() ?? "" }; Save(); Log("Launcher paths saved."); }
        catch (Exception ex) { Log("Could not save paths: " + ex.Message); }
    }
    private void RenderAccounts()
    {
        AccountList.Children.Clear();
        statuses.Clear();
        characterMaintenance.Clear();
        characterStates.Clear();
        characterWeather.Clear();
        var accounts = configuration.ConfiguredAccounts;
        if (configurationReadable && accounts.Any(a => configuration.CharacterArtwork?.GetValueOrDefault(a.Id) is not {} file || !ThemeCatalog.Landscapes.Contains(file)))
        {
            var choices = accounts.ToDictionary(a => a.Id, a => configuration.CharacterArtwork?.GetValueOrDefault(a.Id) is {} file && ThemeCatalog.Landscapes.Contains(file) ? file : ThemeCatalog.DefaultLandscape(a.Id));
            try { var next = configuration with { CharacterArtwork = choices }; store.Save(next); configuration = next; }
            catch (Exception ex) { Log("Could not save character artwork: " + ex.Message); }
        }
        selectedAccounts.IntersectWith(accounts.Select(a => a.Id));
        EmptyState.IsVisible = accounts.Count == 0;
        AccountSummary.Text = $"Accounts · {accounts.Count} configured";
        foreach (var account in accounts)
        {
            var body = new StackPanel { Spacing = 8 };
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var identity = new StackPanel { Spacing = 4 };
            var select = new CheckBox { Name = "SelectAccount_" + account.Id.ToString("N"), Content = "Select", IsChecked = selectedAccounts.Contains(account.Id) };
            select.IsCheckedChanged += (_, _) =>
            {
                if (updatingSelection) return;
                if (select.IsChecked == true) selectedAccounts.Add(account.Id); else selectedAccounts.Remove(account.Id);
                UpdateSelectionControls();
            };
            var dragHandle = new Border
            {
                Name = "Reorder_" + account.Id.ToString("N"), Background = ThemeCatalog.BrushFor("Inset"), CornerRadius = new CornerRadius(5),
                Cursor = new Cursor(StandardCursorType.SizeAll), Padding = new Thickness(10, 6),
                Child = new TextBlock { Text = "↕ Drag to reorder", Foreground = ThemeCatalog.BrushFor("Accent"), FontSize = 12, FontWeight = FontWeight.SemiBold }
            };
            ToolTip.SetTip(dragHandle, "Drag to set character and automatic startup order. Order is saved. Available when account operations finish.");
            identity.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { dragHandle, select } });
            identity.Children.Add(new TextBlock { Text = account.Label, FontSize = 18, FontWeight = FontWeight.SemiBold });
            identity.Children.Add(new TextBlock { Text = (string.IsNullOrWhiteSpace(account.Character) ? "Character not selected" : account.Character) + " · " + (string.IsNullOrWhiteSpace(account.SquareEnixId) ? "SE ID needs configuration" : "SE ID: " + account.SquareEnixId), Foreground = string.IsNullOrWhiteSpace(account.SquareEnixId) ? ThemeCatalog.BrushFor("Danger") : ThemeCatalog.BrushFor("Muted"), TextWrapping = TextWrapping.Wrap });
            identity.Children.Add(new TextBlock { Text = (string.IsNullOrWhiteSpace(account.World) ? "World needs confirmation" : account.World) + " / " + (string.IsNullOrWhiteSpace(account.Profile) ? "Automatic / default profile" : account.Profile), Foreground = ThemeCatalog.BrushFor("Muted"), TextWrapping = TextWrapping.Wrap });
            header.Children.Add(identity);
            var settings = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            Grid.SetColumn(settings, 1);
            header.Children.Add(settings);
            var autoToggle = new ToggleSwitch { Content = "Auto login", IsChecked = account.AutoLoginEnabled };
            var maintainToggle = new ToggleSwitch { Content = "Maintain connection", IsChecked = account.MaintainConnection };
            settings.Children.Add(autoToggle);
            settings.Children.Add(maintainToggle);
            var scheduleToggle = new ToggleSwitch { Content = "Schedule server maintenance", IsChecked = account.ScheduleMaintenance };
            settings.Children.Add(scheduleToggle);
            var updatingSchedule = false;
            scheduleToggle.IsCheckedChanged += (_, _) =>
            {
                if (updatingSchedule) return;
                updatingSchedule = true;
                try
                {
                    var next = configuration with { Accounts = configuration.ConfiguredAccounts.Select(a => a.Id == account.Id ? a with { ScheduleMaintenance = scheduleToggle.IsChecked == true } : a).ToList() };
                    if (!configurationReadable) throw new InvalidOperationException("Configuration could not be loaded.");
                    store.Save(next); configuration = next;
                    Log(account.Label + ": server maintenance scheduling " + (scheduleToggle.IsChecked == true ? "enabled. With Maintain connection enabled, shuts down 5 minutes before and queues login after the end plus 2 minutes. Automatic downtime stays paused until the Windows login adapter is ready." : "disabled."));
                }
                catch (Exception ex) { scheduleToggle.IsChecked = configuration.ConfiguredAccounts.Single(a => a.Id == account.Id).ScheduleMaintenance; Log("Could not save maintenance scheduling: " + ex.Message); }
                finally { updatingSchedule = false; }
            };
            var updating = false;
            void UpdatePolicy(bool auto, bool maintain)
            {
                if (updating) return;
                var previous = configuration;
                updating = true;
                try
                {
                    var next = configuration with { Accounts = configuration.ConfiguredAccounts.Select(a => a.Id == account.Id ? a with { AutoLogin = auto, MaintainConnection = maintain } : a).ToList() };
                    if (!configurationReadable) throw new InvalidOperationException("Configuration could not be loaded.");
                    store.Save(next);
                    configuration = next;
                    autoToggle.IsChecked = auto;
                    maintainToggle.IsChecked = maintain;
                    if (!auto) { intentionallyStopped.Add(account.Id); if (accountCancellation.TryGetValue(account.Id, out var cancellation)) cancellation.Cancel(); }
                    Log(account.Label + (auto ? (maintain ? ": auto login and maintenance enabled." : ": auto login enabled; no automatic reconnect.") : ": auto login and maintenance disabled; game stays open."));
                }
                catch (Exception ex)
                {
                    var original = previous.ConfiguredAccounts.Single(a => a.Id == account.Id);
                    autoToggle.IsChecked = original.AutoLoginEnabled;
                    maintainToggle.IsChecked = original.MaintainConnection;
                    Log("Could not save account settings: " + ex.Message);
                }
                finally { updating = false; }
            }
            autoToggle.IsCheckedChanged += (_, _) => UpdatePolicy(autoToggle.IsChecked == true, autoToggle.IsChecked == true && maintainToggle.IsChecked == true);
            maintainToggle.IsCheckedChanged += (_, _) => UpdatePolicy(autoToggle.IsChecked == true || maintainToggle.IsChecked == true, maintainToggle.IsChecked == true);
            body.Children.Add(header);
            var schedule = new TextBlock { Name = "CharacterMaintenance_" + account.Id.ToString("N"), Foreground = ThemeCatalog.BrushFor("Muted"), TextWrapping = TextWrapping.Wrap };
            characterMaintenance[account.Id] = schedule;
            body.Children.Add(new Border { Background = ThemeCatalog.BrushFor("Inset"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6), Child = schedule });
            var status = new TextBlock { Text = "Ready to launch · " + (vault.Contains(account.Id) ? "Credentials saved" : "No saved credentials"), Foreground = ThemeCatalog.BrushFor("Accent") };
            statuses[account.Id] = status;
            body.Children.Add(status);
            var state = new TextBlock { Name = "CharacterState_" + account.Id.ToString("N"), TextWrapping = TextWrapping.Wrap, Foreground = ThemeCatalog.BrushFor("Muted") };
            characterStates[account.Id] = state;
            body.Children.Add(state);
            var weather = new StackPanel { Name = "Weather_" + account.Id.ToString("N"), Orientation = Orientation.Horizontal, Spacing = 4 };
            characterWeather[account.Id] = weather;
            body.Children.Add(weather);
            var actions = new WrapPanel { Orientation = Orientation.Horizontal };
            var launch = new Button { Name = "Launch_" + account.Id.ToString("N"), Content = "Launch profile", IsEnabled = platform.CanLaunch && configurationReadable && !LoginOperationActive };
            ToolTip.SetTip(launch, "Uses the selected Windower profile. Auto login On completes login; Off opens POL for manual login.");
            launch.Click += async (_, _) => await LaunchAccountFromControl(account.Id);
            var edit = new Button { Content = "Edit account" };
            edit.Click += async (_, _) => await EditAccount(configuration.ConfiguredAccounts.Single(a => a.Id == account.Id));
            var delete = new Button { Content = "Remove" };
            delete.Click += async (_, _) =>
            {
                if (sessions.ContainsKey(account.Id) || pending.Contains(account.Id) || uncertain.Contains(account.Id)) { Log("Cannot remove a tracked or unresolved session. Close the game and restart the manager first."); return; }
                var confirm = new Window { Title = "Remove account", Width = 440, Height = 180, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                var yes = new Button { Content = "Remove account and credentials" };
                yes.Click += (_, _) => confirm.Close(true);
                confirm.Content = new StackPanel { Margin = new Thickness(20), Spacing = 16, Children = { new TextBlock { Text = "Remove " + account.Label + "?", TextWrapping = TextWrapping.Wrap }, yes } };
                if (await confirm.ShowDialog<bool>(this))
                {
                    try
                    {
                        configuration = configuration with { Accounts = configuration.ConfiguredAccounts.Where(a => a.Id != account.Id).ToList() };
                        Save(); vault.Delete(account.Id); RenderAccounts(); Log("Account removed.");
                    }
                    catch (Exception ex) { Log("Could not remove account: " + ex.Message); }
                }
            };
            var stop = new Button { Name = "Shutdown_" + account.Id.ToString("N"), Content = "Graceful shutdown", IsEnabled = platform.CanLaunch && !shuttingDown.Contains(account.Id) };
            stop.Click += async (_, _) => { if (shuttingDown.Contains(account.Id)) await CancelShutdownAccount(account.Id); else await ShutdownAccount(account.Id); };
            var lookup = new Button { Content = "FFXIAH", IsEnabled = WorldCatalog.Normalize(account.World) != null };
            lookup.Click += (_, _) =>
            {
                try
                {
                    var current = configuration.ConfiguredAccounts.Single(a => a.Id == account.Id);
                    var url = "https://www.ffxiah.com/player/" + Uri.EscapeDataString(current.World) + "/" + Uri.EscapeDataString(current.Character);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch (Exception ex) { Log("Could not open character lookup: " + ex.Message); }
            };
            var force = new Button { Content = "Force close", IsEnabled = platform.CanLaunch && sessions.TryGetValue(account.Id, out var forceTarget) && platform.Inspect(forceTarget) != SessionHealth.Exited };
            force.Click += async (_, _) => await ForceCloseAccount(account.Id);
            actions.Children.Add(launch); actions.Children.Add(edit); actions.Children.Add(stop); actions.Children.Add(force); actions.Children.Add(lookup); actions.Children.Add(delete);
            foreach (var button in actions.Children.OfType<Button>()) { button.Padding = new Thickness(10, 6); button.Margin = new Thickness(0, 0, 6, 4); button.FontSize = 12; }
            body.Children.Add(actions);
            var card = new Border { Tag = account.Id, Background = ThemeCatalog.BrushFor("Surface"), CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 12), Child = body };
            if (configuration.LandscapeCards && ThemeCatalog.Resolve(configuration.Theme).Artwork != null)
            {
                var selectedArt = configuration.CharacterArtwork?.GetValueOrDefault(account.Id);
                var file = selectedArt != null && ThemeCatalog.Landscapes.Contains(selectedArt) ? selectedArt : ThemeCatalog.DefaultLandscape(account.Id);
                var surface = new Grid();
                surface.Children.Add(new Border { Background = new ImageBrush(LandscapeBitmap(file)) { Stretch = Stretch.UniformToFill }, IsHitTestVisible = false });
                surface.Children.Add(new Border { Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative), GradientStops = new GradientStops { new GradientStop(Color.Parse("#F0101820"), 0), new GradientStop(Color.Parse("#E0101820"), .7), new GradientStop(Color.Parse("#90101820"), 1) } }, IsHitTestVisible = false });
                body.Margin = new Thickness(16, 12);
                card.Child = null;
                surface.Children.Add(body);
                card.Padding = new Thickness(0); card.ClipToBounds = true; card.Child = surface;
            }
            ConfigureAccountDrag(dragHandle, card, account.Id);
            AccountList.Children.Add(card);
        }
        UpdateSelectionControls();
        RefreshCharacterMaintenance();
        RefreshSessions();
    }
    private void ConfigureAccountDrag(Control handle, Border card, Guid id)
    {
        Point? start = null;
        var dragging = false;
        RenderTargetBitmap? snapshot = null;
        Border? ghost = null;
        Border[] dragCards = [];
        void ClearDrag()
        {
            start = null;
            dragging = false;
            card.Opacity = 1;
            DragLayer.Children.Clear();
            ghost = null;
            snapshot?.Dispose();
            snapshot = null;
            foreach (var other in dragCards) { other.RenderTransform = null; other.BorderThickness = new Thickness(0); }
            dragCards = [];
        }
        handle.PointerPressed += (_, e) =>
        {
            if (LoginOperationActive || !configurationReadable || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
            start = e.GetPosition(AccountList);
            e.Pointer.Capture(handle);
            e.Handled = true;
        };
        handle.PointerMoved += (_, e) =>
        {
            if (start is not {} origin) return;
            var point = e.GetPosition(AccountList);
            if (!dragging && Math.Abs(point.Y - origin.Y) < 6) return;
            if (!dragging)
            {
                snapshot = new RenderTargetBitmap(new PixelSize(Math.Max(1, (int)card.Bounds.Width), Math.Max(1, (int)card.Bounds.Height)));
                snapshot.Render(card);
                dragCards = AccountList.Children.OfType<Border>().ToArray();
                foreach (var other in dragCards.Where(b => !ReferenceEquals(b, card)))
                    other.RenderTransform = new TranslateTransform { Transitions = new Transitions { new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(140) } } };
                ghost = new Border { Name = "DragGhost", Width = card.Bounds.Width, Height = card.Bounds.Height,
                    Opacity = 0.95, CornerRadius = new CornerRadius(10), ClipToBounds = true,
                    BorderBrush = ThemeCatalog.BrushFor("Accent"), BorderThickness = new Thickness(2),
                    BoxShadow = BoxShadows.Parse("0 8 24 0 #90000000"), Child = new Image { Source = snapshot, Stretch = Stretch.Fill } };
                DragLayer.Children.Add(ghost);
                dragging = true;
            }
            card.Opacity = 0;
            var viewport = e.GetPosition(AccountScroll);
            if (viewport.Y < 40) AccountScroll.Offset = AccountScroll.Offset.WithY(Math.Max(0, AccountScroll.Offset.Y - 20));
            else if (viewport.Y > AccountScroll.Bounds.Height - 40) AccountScroll.Offset = AccountScroll.Offset.WithY(AccountScroll.Offset.Y + 20);
            point = e.GetPosition(AccountList);
            var cardPosition = card.TranslatePoint(new Point(0, 0), DragLayer) ?? default;
            var viewportPosition = AccountScroll.TranslatePoint(new Point(0, 0), DragLayer) ?? default;
            Canvas.SetLeft(ghost!, cardPosition.X);
            Canvas.SetTop(ghost!, Math.Clamp(cardPosition.Y + point.Y - origin.Y, viewportPosition.Y,
                Math.Max(viewportPosition.Y, viewportPosition.Y + AccountScroll.Bounds.Height - card.Bounds.Height)));
            var before = dragCards.FirstOrDefault(b => !ReferenceEquals(b, card) && point.Y < b.Bounds.Center.Y);
            var preview = dragCards.Where(b => !ReferenceEquals(b, card)).ToList();
            preview.Insert(before == null ? preview.Count : preview.IndexOf(before), card);
            var nextY = dragCards[0].Bounds.Y;
            foreach (var other in preview)
            {
                if (!ReferenceEquals(other, card) && other.RenderTransform is TranslateTransform slide)
                    slide.Y = nextY - other.Bounds.Y;
                nextY += other.Bounds.Height + AccountList.Spacing;
            }
            e.Handled = true;
        };
        handle.PointerReleased += (_, e) =>
        {
            if (start == null) return;
            var point = e.GetPosition(AccountList);
            var viewport = e.GetPosition(AccountScroll);
            var drop = dragging && point.X >= 0 && point.X <= AccountList.Bounds.Width && viewport.Y >= 0 && viewport.Y <= AccountScroll.Bounds.Height;
            var before = AccountList.Children.OfType<Border>().FirstOrDefault(b => !ReferenceEquals(b, card) && point.Y < b.Bounds.Center.Y)?.Tag as Guid?;
            ClearDrag();
            e.Pointer.Capture(null);
            if (drop) ReorderAccount(id, before);
            e.Handled = true;
        };
        handle.PointerCaptureLost += (_, _) => ClearDrag();
    }
    private void ReorderAccount(Guid id, Guid? before)
    {
        if (!configurationReadable || LoginOperationActive) return;
        var accounts = configuration.ConfiguredAccounts.ToList();
        var account = accounts.FirstOrDefault(a => a.Id == id);
        if (account == null || before == id) return;
        accounts.Remove(account);
        var index = before == null ? accounts.Count : accounts.FindIndex(a => a.Id == before);
        if (index < 0) return;
        accounts.Insert(index, account);
        if (accounts.Select(a => a.Id).SequenceEqual(configuration.ConfiguredAccounts.Select(a => a.Id))) return;
        try
        {
            var next = configuration with { Accounts = accounts };
            store.Save(next);
            configuration = next;
            RenderAccounts();
            Log("Character order saved. Automatic startup follows this order.");
        }
        catch (Exception ex) { Log("Could not save character order: " + ex.Message); }
    }
    private async void AddAccount(object? sender, RoutedEventArgs args) => await EditAccount(null);
    private async Task EditAccount(AccountConfiguration? account)
    {
        if (!configurationReadable) { Log("Repair accounts.json before editing accounts."); return; }
        if (account != null && (pending.Contains(account.Id) || accountCancellation.ContainsKey(account.Id))) { Log("Wait for the current launch or login to finish before editing."); return; }
        var dialog = new AccountDialog(account, vault.IsAvailable, account != null && vault.Contains(account.Id), WindowsInstallationDiscovery.ReadProfiles(configuration.WindowerPath));
        var result = await dialog.ShowDialog<AccountEdit?>(this);
        if (result == null) return;
        if (account != null && sessions.TryGetValue(account.Id, out var active) && platform.Inspect(active) != SessionHealth.Exited &&
            !result.Account.Character.Equals(account.Character, StringComparison.OrdinalIgnoreCase))
        { Log("A running account’s character cannot be changed. Other account details and credentials can be configured while it stays online."); return; }
        try
        {
            if (result.Credentials != null)
            {
                var previous = vault.Read(result.Account.Id);
                vault.Save(result.Account.Id, new AccountCredentials(
                    string.IsNullOrEmpty(result.Credentials.PolPassword) ? previous?.PolPassword ?? "" : result.Credentials.PolPassword,
                    string.IsNullOrEmpty(result.Credentials.SquareEnixPassword) ? previous?.SquareEnixPassword ?? "" : result.Credentials.SquareEnixPassword,
                    string.IsNullOrEmpty(result.Credentials.MemberPassword) ? previous?.MemberPassword ?? "" : result.Credentials.MemberPassword));
            }
            if (result.DeleteCredentials) vault.Delete(result.Account.Id);
            configuration = configuration with { Accounts = configuration.ConfiguredAccounts.Where(a => a.Id != result.Account.Id).Append(result.Account).ToList() };
            Save(); RenderAccounts(); Log(result.Account.Label + ": account saved.");
        }
        catch (Exception ex) { Log("Could not save account: " + ex.Message); }
    }
    private string? ManualLoginProblem(AccountConfiguration account) =>
        (autoLogin is WindowsAutoLogin windows ? windows.SetupProblem(account) : autoLogin.ReadinessProblem(account)) ??
        (!vault.Contains(account.Id) ? "Save this account's Square Enix password first." : null);
    private bool LoginOperationActive => batchLaunching || accountCancellation.Count > 0 || pending.Count > 0 || shuttingDown.Count > 0 || shutdownCancelling.Count > 0 || forceClosing.Count > 0;
    private bool CanAutoLogin => platform.CanLaunch && configurationReadable && !LoginOperationActive && uncertain.Count == 0 &&
        configuration.ConfiguredAccounts.Any(a => a.AutoLoginEnabled && ManualLoginProblem(a) == null);

    private async Task<CalibrationOutcome> HandleCalibration(CalibrationRequest request, CancellationToken cancellation)
    {
        if (request.Operation == "launch") return await LaunchCalibration(request.Account, cancellation);
        if (request.Operation is "inspect" or "login" or "train-member" or "train-password" or "train-play" or "train-character" or "continue-login" or "return-game-menu" or "train-character-success") return await CalibrateLogin(request.Account, request.Process, request.Operation == "inspect", cancellation, request.Operation == "train-member", request.PressMs, request.PollMs,
            request.Operation switch { "train-password" => "password", "train-play" => "play", "train-character" => "character", "continue-login" => "continue", "return-game-menu" => "return", "train-character-success" => "success", _ => "" }, request.MenuGapMs);
        if (!configurationReadable || !WindowsUnattendedSetup.IsElevated || !configuration.ConfiguredAccounts.Any(a => a.Id == request.Account) ||
            !sessions.TryGetValue(request.Account, out var owned) || owned != request.Process)
            return new(false, "This operation requires the account's exact tracked process.");
        if (LoginOperationActive) return new(false, "Wait for the current account operation to finish.");
        cancellation.ThrowIfCancellationRequested();
        var targets = request.Operation == "shutdown_all" ? sessions.Values.ToArray() : [owned];
        switch (request.Operation)
        {
            case "close": await ForceCloseAccount(request.Account); break;
            case "shutdown": await ShutdownAccount(request.Account); break;
            case "shutdown_all": await ShutdownAccounts(sessions.Keys.ToArray()); break;
            default: return new(false, "Unsupported account operation.");
        }
        var exited = targets.All(p => platform.Inspect(p) == SessionHealth.Exited);
        return new(exited, exited ? "Requested managed process exit confirmed." : "One or more original managed processes remain open; no unrelated process was targeted.");
    }

    private async Task<CalibrationOutcome> CalibrateLogin(Guid id, ManagedProcess process, bool inspect, CancellationToken cancellation, bool memberSectionOnly = false, int pressMs = 80, int pollMs = 50, string trainingSection = "", int menuGapMs = 0)
    {
        if (!configurationReadable || autoLogin is not WindowsAutoLogin login || !WindowsUnattendedSetup.IsElevated)
            return new(false, "Start the installed manager from its unattended shortcut.");
        if (LoginOperationActive)
            return new(false, "Wait for the current account operation to finish.");
        var account = configuration.ConfiguredAccounts.SingleOrDefault(a => a.Id == id);
        if (account == null) return new(false, "Configured account no longer exists.");
        if (sessions.Any(s => s.Key != id && s.Value == process) || sessions.TryGetValue(id, out var existing) && existing != process && platform.Inspect(existing) != SessionHealth.Exited)
            return new(false, "POL is already tracked by another account or this account has a different live session.");
        if (!inspect && LoginCheckpoints.Requirements(account) is {} problem) return new(false, problem);
        if (!inspect && !memberSectionOnly && trainingSection is not ("play" or "continue" or "character" or "return" or "success") && !vault.Contains(id)) return new(false, "Save this account's Square Enix password first.");
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(cancellation, shutdown.Token);
        accountCancellation[id] = cancelled;
        UpdateSelectionControls();
        try
        {
            if (!inspect && !memberSectionOnly && trainingSection is not ("play" or "continue" or "character" or "return" or "success"))
            {
                maintenance.Worlds = configuration.ConfiguredAccounts.Select(a => a.World).Distinct().ToArray();
                await maintenance.RefreshAsync(true, cancelled.Token);
                var decision = RecoveryPolicy.Evaluate(account with { MaintainConnection = true }, false, maintenance.Snapshot, DateTimeOffset.UtcNow);
                if (!decision.Allowed) return new(false, decision.Reason);
                if (InGameMaintenance.Hold(GameAlerts(account), maintenance.Snapshot, DateTimeOffset.UtcNow) is {} hold) return new(false, hold.Reason);
            }
            var full = await login.RequestAsync(account, process, inspect, cancelled.Token, memberSectionOnly, pressMs, pollMs, trainingSection, menuGapMs);
            if (inspect && calibrationControl != null) File.Copy(login.CapturePath!, calibrationControl.CapturePath, true);
            if (!inspect)
            {
                RememberSession(id, process);
                if (!memberSectionOnly && trainingSection == "") intentionallyStopped.Remove(id);
                if (full) WindowsLoginValidation.Record(dataRoot, account);
            }
            return new(true, inspect ? "Verified POL client captured." : memberSectionOnly || trainingSection != "" ? login.LastSectionResult ?? "Section completed without a full login proof." : full ? "Complete login verified; automatic login enabled for this account." : "Character is already online. Start from the POL member list to validate the complete login flow.", full);
        }
        catch (OperationCanceledException) { return new(false, "Calibration cancelled."); }
        catch (Exception ex) { return new(false, ex is InvalidOperationException ? ex.Message : "Calibration stopped. Inspect POL before retrying."); }
        finally { accountCancellation.Remove(id); UpdateSelectionControls(); }
    }

    private async Task<CalibrationOutcome> LaunchCalibration(Guid id, CancellationToken cancellation)
    {
        if (!configurationReadable || !WindowsUnattendedSetup.IsElevated) return new(false, "Start the installed manager from its unattended shortcut.");
        if (LoginOperationActive || uncertain.Count > 0)
            return new(false, "Another launch or login needs attention before starting a new session.");
        var account = configuration.ConfiguredAccounts.SingleOrDefault(a => a.Id == id);
        if (account == null) return new(false, "Configured account no longer exists.");
        if (sessions.TryGetValue(id, out var existing) && platform.Inspect(existing) != SessionHealth.Exited)
            return new(true, "Using this account's tracked POL session.", Process: existing);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(cancellation, shutdown.Token);
        accountCancellation[id] = cancelled;
        pending.Add(id);
        try
        {
            maintenance.Worlds = configuration.ConfiguredAccounts.Select(a => a.World).Distinct().ToArray();
            await maintenance.RefreshAsync(true, cancelled.Token);
            var decision = RecoveryPolicy.Evaluate(account with { MaintainConnection = true }, false, maintenance.Snapshot, DateTimeOffset.UtcNow);
            if (!decision.Allowed) return new(false, decision.Reason);
            if (InGameMaintenance.Hold(GameAlerts(account), maintenance.Snapshot, DateTimeOffset.UtcNow) is {} hold) return new(false, hold.Reason);
            if (sessions.Any(s => s.Key != id && AccountIdentity.Conflicts(account, configuration.ConfiguredAccounts.Single(a => a.Id == s.Key)) && platform.Inspect(s.Value) != SessionHealth.Exited))
                return new(false, "This POL account or character is already running.");
            var process = await platform.LaunchAsync(configuration, account, sessions.Values.ToArray(), cancelled.Token);
            RememberSession(id, process);
            intentionallyStopped.Remove(id);
            return new(true, "POL launched using the manager's elevated token.", Process: process);
        }
        catch (Exception ex)
        {
            if (ex is LaunchUncertainException) uncertain.Add(id);
            return new(false, ex is OperationCanceledException ? "Launch cancelled." : ex.Message);
        }
        finally { pending.Remove(id); accountCancellation.Remove(id); UpdateSelectionControls(); }
    }

    private void UpdateSelectionControls()
    {
        updatingSelection = true;
        try
        {
            var count = selectedAccounts.Count;
            SelectionSummary.Text = $"{count} selected";
            ThemePicker.IsEnabled = configurationReadable && !LoginOperationActive;
            LandscapeCardsToggle.IsEnabled = configurationReadable && !LoginOperationActive && ThemeCatalog.Resolve(configuration.Theme).Artwork != null;
            ShuffleArtworkButton.IsEnabled = LandscapeCardsToggle.IsEnabled && configuration.LandscapeCards && configuration.ConfiguredAccounts.Count > 0;
            BulkActions.Header = $"Bulk actions · {count} selected";
            SelectAllAccounts.IsChecked = count == 0 ? false : count == configuration.ConfiguredAccounts.Count ? true : null;
            SelectAllAccounts.IsEnabled = configurationReadable && configuration.ConfiguredAccounts.Count > 0;
            foreach (var select in AccountList.GetLogicalDescendants().OfType<CheckBox>())
                if (select.Name?.StartsWith("SelectAccount_", StringComparison.Ordinal) == true && Guid.TryParseExact(select.Name[14..], "N", out var id)) select.IsChecked = selectedAccounts.Contains(id);
            LaunchSelectedButton.IsEnabled = count > 0 && platform.CanLaunch && configurationReadable && !batchLaunching;
            ShutdownSelectedButton.IsEnabled = count > 0 && platform.CanLaunch && configurationReadable;
            ShutdownAllButton.IsEnabled = platform.CanLaunch && configurationReadable;
            CancelShutdownsButton.IsEnabled = shutdownStops.Count > 0;
            ApplyBulkPolicyButton.IsEnabled = count > 0 && configurationReadable;
            LaunchEnabledButton.IsEnabled = platform.CanLaunch && configurationReadable && !batchLaunching;
            StartEnabledButton.IsEnabled = CanAutoLogin;
            foreach (var launch in AccountList.GetLogicalDescendants().OfType<Button>().Where(b => b.Name?.StartsWith("Launch_", StringComparison.Ordinal) == true))
                launch.IsEnabled = platform.CanLaunch && configurationReadable && !LoginOperationActive;
            StartEnabledButton.Content = LoginOperationActive ? "Account operation in progress…" : "Auto login enabled accounts";
            if (platform.CanLaunch)
            {
                var inputActive = batchLaunching || accountCancellation.Count > 0;
                Footer.Text = inputActive
                    ? "AUTO LOGIN IN PROGRESS — leave the keyboard and mouse idle. Your input can reach POL and interrupt login."
                    : "Credentials stay encrypted on this Windows user account. Closing the manager leaves games running.";
                Footer.Foreground = ThemeCatalog.BrushFor(inputActive ? "Warning" : "Muted");
            }
            var problems = configuration.ConfiguredAccounts.Where(a => a.AutoLoginEnabled)
                .Select(a => (Account: a, Problem: ManualLoginProblem(a))).Where(p => p.Problem != null)
                .Select(p => p.Account.Label + ": " + p.Problem).ToArray();
            ToolTip.SetTip(StartEnabledButton, LoginOperationActive ? "Wait for the current launch, login or shutdown to finish." :
                uncertain.Count > 0 ? "Resolve the uncertain launch before starting another account." :
                !platform.CanLaunch ? "Login automation requires Windows." :
                !configurationReadable ? "Repair the configuration before starting accounts." :
                problems.Length > 0 ? string.Join("\n", problems) :
                !configuration.ConfiguredAccounts.Any(a => a.AutoLoginEnabled) ? "Enable Auto login for at least one account." :
                "Log in configured accounts and verify each character. A complete successful login also validates unattended recovery.");
        }
        finally { updatingSelection = false; }
    }
    private void SelectAllChanged(object? sender, RoutedEventArgs args)
    {
        if (updatingSelection || !configurationReadable) return;
        selectedAccounts.Clear();
        if (SelectAllAccounts.IsChecked != false) selectedAccounts.UnionWith(configuration.ConfiguredAccounts.Select(a => a.Id));
        UpdateSelectionControls();
    }
    private void ClearSelection(object? sender, RoutedEventArgs args) { selectedAccounts.Clear(); UpdateSelectionControls(); }
    private void ApplyBulkPolicy(object? sender, RoutedEventArgs args)
    {
        if (!configurationReadable || selectedAccounts.Count == 0) return;
        var operation = BulkPolicy.SelectedIndex;
        if (operation < 0 || operation > 5) return;
        try
        {
            var next = configuration with { Accounts = configuration.ConfiguredAccounts.Select(a => !selectedAccounts.Contains(a.Id) ? a : operation switch
            {
                0 => a with { AutoLogin = true },
                1 => a with { AutoLogin = false, MaintainConnection = false },
                2 => a with { AutoLogin = true, MaintainConnection = true },
                3 => a with { AutoLogin = a.AutoLoginEnabled, MaintainConnection = false },
                4 => a with { ScheduleMaintenance = true },
                _ => a with { ScheduleMaintenance = false }
            }).ToList() };
            store.Save(next); configuration = next;
            foreach (var id in selectedAccounts)
            {
                if (operation == 1) intentionallyStopped.Add(id);
                if (operation is 1 or 3 && accountCancellation.TryGetValue(id, out var cancellation)) cancellation.Cancel();
                if (operation is 0 or 2) intentionallyStopped.Remove(id);
            }
            var label = (BulkPolicy.SelectedItem as ComboBoxItem)?.Content;
            Log($"{label}: applied to {selectedAccounts.Count} account(s)." + (operation is 0 or 2 ? " Automatic login is pending Windows validation." : ""));
            RenderAccounts();
        }
        catch (Exception ex) { Log("Could not save bulk settings: " + ex.Message); }
    }
    private async void LaunchEnabled(object? sender, RoutedEventArgs args) =>
        await LaunchProfiles(configuration.ConfiguredAccounts.Where(a => a.AutoLoginEnabled).Select(a => a.Id).ToArray(), enabledOnly: true);
    private async void LaunchSelected(object? sender, RoutedEventArgs args) => await LaunchProfiles(selectedAccounts.ToArray(), enabledOnly: false);
    private async Task LaunchProfiles(Guid[] ids, bool enabledOnly)
    {
        if (batchLaunching || !configurationReadable || !platform.CanLaunch) return;
        if (ids.Length == 0) { Log(enabledOnly ? "No accounts have Auto login enabled. Select accounts and use Open selected profiles, or enable Auto login." : "Select accounts first."); return; }
        batchLaunching = true;
        foreach (var id in ids) intentionallyStopped.Remove(id);
        UpdateSelectionControls();
        try
        {
            foreach (var id in ids)
            {
                if (shutdown.IsCancellationRequested) break;
                if (!configuration.ConfiguredAccounts.Any(a => a.Id == id && (!enabledOnly || a.AutoLoginEnabled) && !intentionallyStopped.Contains(id))) continue;
                await LaunchAccount(id);
                if (uncertain.Count > 0) { Log("Batch paused: inspect the unresolved launch before starting more accounts."); break; }
            }
        }
        finally { batchLaunching = false; UpdateSelectionControls(); }
    }
    private async Task LaunchAccountFromControl(Guid id)
    {
        if (LoginOperationActive || !configurationReadable || !platform.CanLaunch) return;
        var account = configuration.ConfiguredAccounts.FirstOrDefault(a => a.Id == id);
        if (account == null) return;
        batchLaunching = true;
        intentionallyStopped.Remove(id);
        UpdateSelectionControls();
        try
        {
            if (account.AutoLoginEnabled) await RunAutoLogin(account, validate: true);
            else await LaunchAccount(id);
        }
        finally { batchLaunching = false; UpdateSelectionControls(); }
    }
    private async Task LaunchAccount(Guid id)
    {
        if (shuttingDown.Count > 0 || shutdownCancelling.Count > 0 || forceClosing.Count > 0) { Log("Wait for shutdown or process cleanup to finish before opening another profile."); return; }
        if (shutdown.IsCancellationRequested || intentionallyStopped.Contains(id) || shuttingDown.Contains(id)) return;
        if (uncertain.Count > 0) { Log("An earlier launch is unresolved. Inspect POL sessions and restart the manager before retrying."); return; }
        if (sessions.TryGetValue(id, out var existing) && platform.Inspect(existing) != SessionHealth.Exited) { Log("This account already has a tracked session."); return; }
        if (pending.Count > 0) { Log("Another profile is launching. Wait for it to finish."); return; }
        if (!pending.Add(id)) return;
        var account = configuration.ConfiguredAccounts.Single(a => a.Id == id);
        if (sessions.Keys.Any(other => other != id && configuration.ConfiguredAccounts.Any(a => a.Id == other && AccountIdentity.Conflicts(account, a)) &&
            platform.Inspect(sessions[other]) != SessionHealth.Exited))
        { pending.Remove(id); Log("This POL account or character already has a tracked session."); return; }
        forcedClosed.Remove(id);
        statuses[id].Text = "Starting Windower…";
        try
        {
            RememberSession(id, await platform.LaunchAsync(configuration, account, sessions.Values.ToArray(), shutdown.Token));
            Log(account.Label + ": POL identified and attached to this account.");
        }
        catch (OperationCanceledException) { Log("Queued launch cancelled."); }
        catch (Exception ex)
        {
            if (ex is LaunchUncertainException) uncertain.Add(id);
            Log(account.Label + ": " + ex.Message);
            statuses[id].Text = ex is LaunchUncertainException ? "Needs attention · launch unresolved: " + ex.Message : "Launch blocked · " + ex.Message;
        }
        finally { pending.Remove(id); RefreshSessions(); }
    }
    private void AttachVerifiedSessions()
    {
        if (!configurationReadable || platform is not WindowsSessionPlatform windows || !windows.CanLaunch || AddonDataDirectory is not {} data) return;
        var running = windows.Discover();
        var ipc = new ShutdownIpc(data);
        foreach (var account in configuration.ConfiguredAccounts)
        {
            if (pending.Contains(account.Id) || shuttingDown.Contains(account.Id)) continue;
            if (sessions.TryGetValue(account.Id, out var tracked) && platform.Inspect(tracked) != SessionHealth.Exited) continue;
            if (configuration.ConfiguredAccounts.Count(a => a.Character.Equals(account.Character, StringComparison.OrdinalIgnoreCase)) != 1) continue;
            var candidates = running.Where(p => p.Character != null && p.Character.Equals(account.Character, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (candidates.Length != 1) continue;
            try
            {
                var process = candidates[0].Process;
                var game = string.IsNullOrWhiteSpace(account.World)
                    ? ipc.DiscoverCharacter(account.Character, new DateTimeOffset(process.StartedUtc), DateTimeOffset.UtcNow)
                    : ipc.ReadSession(account, DateTimeOffset.UtcNow);
                if (game == null || game.RegisteredUtc < new DateTimeOffset(process.StartedUtc).AddSeconds(-2)) continue;
                RememberSession(account.Id, process);
                forcedClosed.Remove(account.Id);
                Log(account.Label + ": attached to running character with fresh " + game.World + " addon heartbeat.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException) { }
        }
    }
    private void RefreshSessions()
    {
        RefreshCharacterStates();
        foreach (var button in AccountList.GetLogicalDescendants().OfType<Button>())
            if (button.Name?.StartsWith("Shutdown_", StringComparison.Ordinal) == true && Guid.TryParseExact(button.Name[9..], "N", out var id))
            {
                button.IsEnabled = platform.CanLaunch && !shutdownCancelling.Contains(id);
                button.Content = shutdownCancelling.Contains(id) ? "Cancelling shutdown…" : shuttingDown.Contains(id) ? "Cancel shutdown" : "Graceful shutdown";
            }
        CancelShutdownsButton.IsEnabled = shutdownStops.Count > 0;
        foreach (var (id, process) in sessions.ToArray())
        {
            if (!statuses.TryGetValue(id, out var status) || pending.Contains(id) || accountCancellation.ContainsKey(id)) continue;
            var health = platform.Inspect(process);
            if (health != SessionHealth.Exited && shutdownProgress.TryGetValue(id, out var progress)) { status.Text = $"PID {process.Pid} · {progress}"; continue; }
            status.Text = health switch
            {
                SessionHealth.Exited => forcedClosed.Contains(id) ? "Stopped · forced cleanup complete" : intentionallyStopped.Contains(id) ? "Intentionally stopped" : RecoveryStatus(id),
                SessionHealth.Unresponsive => $"PID {process.Pid} · window not responding; observing",
                SessionHealth.Unknown => $"PID {process.Pid} · health unavailable",
                _ => shuttingDown.Contains(id) ? $"PID {process.Pid} · waiting for graceful shutdown" : $"PID {process.Pid} · process running; character login unverified"
            };
        }
        foreach (var id in uncertain) if (statuses.TryGetValue(id, out var status)) status.Text = "Needs attention · launch unresolved";
    }

    private Bitmap ElementIcon(string element)
    {
        var key = "weather/" + element;
        if (landscapeArtwork.TryGetValue(key, out var icon)) return icon;
        using var stream = AssetLoader.Open(new Uri("avares://FFXILauncher.Desktop/Assets/Weather/" + element + ".png"));
        return landscapeArtwork[key] = new Bitmap(stream);
    }

    private void RefreshCharacterStates()
    {
        var now = DateTimeOffset.UtcNow;
        var clock = VanaClock.Read(now);
        VanaClockStatus.Text = clock.Describe();
        VanaDayIcon.Source = ElementIcon(clock.DayElement);
        ToolTip.SetTip(VanaDayIcon, clock.Day + " · " + clock.DayElement + " day element");
        var data = string.IsNullOrWhiteSpace(configuration.WindowerPath) ? null :
            Path.Combine(Path.GetDirectoryName(configuration.WindowerPath) ?? "", "addons", "FFXIManager", "data");
        foreach (var account in configuration.ConfiguredAccounts)
        {
            if (!characterStates.TryGetValue(account.Id, out var text)) continue;
            try
            {
                var state = data == null || string.IsNullOrWhiteSpace(account.World) ? null : new CharacterStateReader(data).Read(account, now);
                var heartbeat = state == null ? null : new ShutdownIpc(data!).ReadSession(account, now);
                var live = state != null && state.LoggedOutUtc == null && heartbeat?.Nonce == state.Nonce && now - state.ReportedUtc <= TimeSpan.FromSeconds(75);
                text.Text = state?.Describe(live, includeEnvironment: false) ?? "Player data pending · deploy/reload FFXIManager";
                if (characterWeather.TryGetValue(account.Id, out var weather))
                {
                    weather.Children.Clear();
                    weather.Children.Add(new TextBlock { Text = live ? "Weather: " : "Last weather: ", Foreground = ThemeCatalog.BrushFor("Muted") });
                    if (state?.Environment is {} environment)
                    {
                        var effect = WeatherEffect.FromName(environment.Weather);
                        if (effect.Element is {} element)
                        {
                            var icon = ElementIcon(element);
                            for (var i = 0; i < effect.Intensity; i++) weather.Children.Add(new Image { Source = icon, Width = 16, Height = 16, Stretch = Stretch.Uniform });
                        }
                        weather.Children.Add(new TextBlock { Text = effect.Describe(environment.Weather) + (!live ? " · stale" : ""), Foreground = ThemeCatalog.BrushFor("Muted") });
                    }
                    else weather.Children.Add(new TextBlock { Text = live ? "pending · reload FFXIManager" : "unknown", Foreground = ThemeCatalog.BrushFor("Muted") });
                }
                text.Foreground = state?.IsDead == true ? ThemeCatalog.BrushFor("Danger") : ThemeCatalog.BrushFor("Muted");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { text.Text = "Player stats unavailable · " + ex.Message; }
        }
    }

    private async void StartEnabled(object? sender, RoutedEventArgs args)
    {
        if (batchLaunching || !CanAutoLogin) return;
        batchLaunching = true;
        UpdateSelectionControls();
        StartEnabledButton.IsEnabled = false;
        LaunchEnabledButton.IsEnabled = false;
        try
        {
            maintenance.Worlds = configuration.ConfiguredAccounts.Select(a => a.World).Distinct().ToArray();
            await maintenance.RefreshAsync(true, shutdown.Token);
            var enabled = configuration.ConfiguredAccounts.Where(a => a.AutoLoginEnabled).Select(a => a.Id).ToArray();
            if (enabled.Length == 0) { Log("Enable Auto login for at least one account."); return; }
            foreach (var id in enabled) intentionallyStopped.Remove(id);
            // Identify each process before opening the next one, then let POL
            // startup overlap. No credential input starts until this pass ends.
            foreach (var id in enabled)
            {
                if (shutdown.IsCancellationRequested || uncertain.Count > 0) return;
                var account = configuration.ConfiguredAccounts.FirstOrDefault(a => a.Id == id);
                if (account == null || !account.AutoLoginEnabled || intentionallyStopped.Contains(id)) continue;
                var decision = EvaluateRecovery(account with { MaintainConnection = true });
                if (!decision.Allowed || ManualLoginProblem(account) != null || !vault.Contains(id)) continue;
                if (!sessions.TryGetValue(id, out var process) || platform.Inspect(process) == SessionHealth.Exited)
                    await LaunchAccount(id);
            }
            foreach (var id in enabled)
            {
                var account = configuration.ConfiguredAccounts.FirstOrDefault(a => a.Id == id);
                if (account == null || !account.AutoLoginEnabled || shutdown.IsCancellationRequested || intentionallyStopped.Contains(id)) continue;
                await RunAutoLogin(account, validate: true);
                if (uncertain.Count > 0) break;
            }
        }
        finally { batchLaunching = false; UpdateSelectionControls(); }
    }
    private async Task RunAutoLogin(AccountConfiguration account, bool validate = false)
    {
        var decision = EvaluateRecovery(account with { MaintainConnection = true });
        if (!decision.Allowed) { statuses[account.Id].Text = decision.Reason + (decision.ResumeUtc is {} resume ? $" until {resume.ToLocalTime():g}" : ""); return; }
        var readiness = validate ? ManualLoginProblem(account) : autoLogin.ReadinessProblem(account);
        if (readiness != null) { statuses[account.Id].Text = readiness; Log(account.Label + ": " + readiness); return; }
        if (!vault.Contains(account.Id)) { statuses[account.Id].Text = "Auto login needs saved credentials"; return; }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        accountCancellation[account.Id] = cancellation;
        UpdateSelectionControls();
        try
        {
            if (!sessions.TryGetValue(account.Id, out var process) || platform.Inspect(process) == SessionHealth.Exited) await LaunchAccount(account.Id);
            if (intentionallyStopped.Contains(account.Id) || !sessions.TryGetValue(account.Id, out process) || platform.Inspect(process) == SessionHealth.Exited) return;
            if (validate && autoLogin is WindowsAutoLogin windows)
            {
                var full = await windows.RequestAsync(account, process, false, cancellation.Token);
                if (full) WindowsLoginValidation.Record(dataRoot, account);
                else Log(account.Label + ": existing login verified; complete startup validation still required for unattended recovery.");
            }
            else await autoLogin.LoginAsync(account, process, vault, cancellation.Token);
            Log(account.Label + ": character login verified.");
        }
        catch (OperationCanceledException) { Log(account.Label + ": automatic login cancelled."); }
        catch (Exception ex) { nextRetry[account.Id] = DateTimeOffset.UtcNow.AddMinutes(5); Log(account.Label + ": login needs attention: " + ex.Message); }
        finally { accountCancellation.Remove(account.Id); UpdateSelectionControls(); }
    }
    private string? AddonDataDirectory => string.IsNullOrWhiteSpace(configuration.WindowerPath) ? null : Path.Combine(Path.GetDirectoryName(configuration.WindowerPath)!, "addons", "FFXIManager", "data");
    private IReadOnlyList<InGameMaintenanceAlert> GameAlerts(AccountConfiguration account) => AddonDataDirectory is {} data ? InGameMaintenance.Read(data, account.World, DateTimeOffset.UtcNow) : [];
    private RecoveryDecision EvaluateRecovery(AccountConfiguration account)
    {
        var decision = RecoveryPolicy.Evaluate(account, intentionallyStopped.Contains(account.Id), maintenance.Snapshot, DateTimeOffset.UtcNow);
        if (intentionallyStopped.Contains(account.Id) || !account.MaintainConnection) return decision;
        return InGameMaintenance.Hold(GameAlerts(account), maintenance.Snapshot, DateTimeOffset.UtcNow) ?? decision;
    }
    private void ClearSelectedMaintenanceAlerts(object? sender, RoutedEventArgs e)
    {
        if (AddonDataDirectory is not {} data) return;
        try
        {
            var worlds = configuration.ConfiguredAccounts.Where(a => selectedAccounts.Contains(a.Id)).Select(a => a.World).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var world in worlds)
                foreach (var alert in InGameMaintenance.Read(data, world, DateTimeOffset.UtcNow))
                    InGameMaintenance.Acknowledge(data, alert);
            RefreshCharacterMaintenance();
            Log("Cleared in-game maintenance alerts for selected worlds. Official scheduled maintenance holds still apply.");
        }
        catch (Exception ex) { Log("Could not clear maintenance alerts: " + ex.Message); }
    }
    private string RecoveryStatus(Guid id)
    {
        var account = configuration.ConfiguredAccounts.Single(a => a.Id == id);
        var decision = EvaluateRecovery(account);
        if (!decision.Allowed) return decision.Reason + (decision.ResumeUtc is {} resume ? $" · resumes after {MaintenanceTimeDisplay.Instant(resume)}" : "");
        return autoLogin.ReadinessProblem(account) ?? "Process exited · reconnect queued";
    }
    private void RefreshCharacterMaintenance()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = maintenance.Snapshot;
        string Describe(MaintenanceWindow window)
        {
            var state = now >= window.StartUtc ? "In progress" : "Upcoming";
            return $"{state} · {MaintenanceTimeDisplay.Range(window.StartUtc, window.EndUtc)}";
        }
        var fresh = snapshot.CheckedUtc is { } checkedAt && now - checkedAt <= TimeSpan.FromMinutes(15);
        foreach (var account in configuration.ConfiguredAccounts)
        {
            if (!characterMaintenance.TryGetValue(account.Id, out var display)) continue;
            var relevant = MaintenanceSchedule.UpcomingForCharacter(snapshot.Windows, account.World, now);
            var next = relevant.FirstOrDefault();
            var global = relevant.FirstOrDefault(w => w.World == null);
            var world = relevant.FirstOrDefault(w => w.World != null);
            var missing = snapshot.ReviewRequired?.Count > 0 ? "Unconfirmed — notice requires review" : fresh ? "None announced" : "Not confirmed — waiting for a fresh notice check";
            var rows = new List<string> { "Global: " + (global == null ? missing : Describe(global)),
                string.IsNullOrWhiteSpace(account.World) ? "World: pending detection" : account.World + ": " + (world == null ? missing : Describe(world)) };
            if (next != null) rows.Insert(0, "Next: " + (next.World ?? "Global") + " · times shown in your local zone");
            ToolTip.SetTip(display, relevant.Count == 0 ? null : string.Join("\n", relevant.Select(w => "Official notice: " + w.Title + "\n" + w.Source)));
            if (!fresh && snapshot.CheckedUtc is { } lastCheck) rows.Add($"Cached schedule · last checked {lastCheck.ToLocalTime():g}; refresh required");
            if (snapshot.ReviewRequired?.Count > 0) rows.Add("Relevant notice(s) need review; schedule may be incomplete: " + string.Join("; ", snapshot.ReviewRequired));
            foreach (var alert in GameAlerts(account))
            {
                var hold = InGameMaintenance.Hold([alert], snapshot, now);
                if (hold == null && AddonDataDirectory is {} resolvedData) InGameMaintenance.Acknowledge(resolvedData, alert);
                rows.Add($"In-game alert · {alert.Character}/{alert.World} · {alert.ObservedUtc.ToLocalTime():g}: {alert.Message}");
                rows.Add(hold == null ? "Posted window finished; reconnect hold released" : hold.Reason + (hold.ResumeUtc is {} resume ? $" until {MaintenanceTimeDisplay.Instant(resume)}" : "; clear selected worlds after review"));
            }
            display.Text = string.Join("\n", rows);
        }
    }
    private async Task CheckRecovery()
    {
        if (recoveryChecking || shutdown.IsCancellationRequested) return;
        recoveryChecking = true;
        try
        {
            CollectCharacterWorlds();
            maintenance.Worlds = configuration.ConfiguredAccounts.Select(a => a.World).Distinct().ToArray();
            await maintenance.RefreshAsync(false, shutdown.Token);
            RefreshCharacterMaintenance();
            MaintenanceStatus.Text = maintenance.Status + "\n" + string.Join("\n", maintenance.Snapshot.Windows.Where(w => w.EndUtc > DateTimeOffset.UtcNow).Select(w => $"{(w.World ?? "Global")}: {MaintenanceTimeDisplay.Range(w.StartUtc, w.EndUtc)}"));
            foreach (var (id, process) in sessions.ToArray())
            {
                var account = configuration.ConfiguredAccounts.FirstOrDefault(a => a.Id == id);
                if (account == null || intentionallyStopped.Contains(id) || shuttingDown.Contains(id) || platform.Inspect(process) != SessionHealth.Responsive) continue;
                var window = MaintenanceSchedule.ShutdownDue(account, maintenance.Snapshot, DateTimeOffset.UtcNow);
                if (window == null || autoLogin.ReadinessProblem(account) != null) continue;
                var key = $"{id}|{process.Pid}|{process.StartedUtc.Ticks}|{window.Source}|{window.StartUtc.UtcTicks}";
                if (!scheduledShutdowns.Add(key)) continue;
                Log(account.Label + ": scheduled maintenance shutdown for " + window.Title);
                await ShutdownAccount(id, scheduled: true);
            }
            if (LoginOperationActive || uncertain.Count > 0) return;
            foreach (var (id, process) in sessions.ToArray())
            {
                var account = configuration.ConfiguredAccounts.FirstOrDefault(a => a.Id == id);
                if (account == null || platform.Inspect(process) != SessionHealth.Exited || autoLogin.ReadinessProblem(account) != null || shuttingDown.Contains(id)) continue;
                if (!EvaluateRecovery(account).Allowed) continue;
                if (nextRetry.TryGetValue(id, out var retry) && DateTimeOffset.UtcNow < retry) continue;
                nextRetry[id] = DateTimeOffset.UtcNow.AddSeconds(15 + Random.Shared.Next(15));
                await RunAutoLogin(account);
                break;
            }
        }
        finally { recoveryChecking = false; }
    }
    private void CollectCharacterWorlds()
    {
        if (!configurationReadable || string.IsNullOrWhiteSpace(configuration.WindowerPath)) return;
        try
        {
            var ipc = new ShutdownIpc(Path.Combine(Path.GetDirectoryName(configuration.WindowerPath)!, "addons", "FFXIManager", "data"));
            var updated = configuration.ConfiguredAccounts.ToList();
            var changed = false;
            foreach (var (id, process) in sessions)
            {
                var index = updated.FindIndex(a => a.Id == id);
                if (index < 0 || platform.Inspect(process) != SessionHealth.Responsive || updated.Count(a => a.Character.Equals(updated[index].Character, StringComparison.OrdinalIgnoreCase)) != 1) continue;
                var game = ipc.DiscoverCharacter(updated[index].Character, new DateTimeOffset(process.StartedUtc), DateTimeOffset.UtcNow);
                var world = WorldCatalog.Normalize(game?.World);
                if (world == null || updated[index].World.Equals(world, StringComparison.OrdinalIgnoreCase)) continue;
                updated[index] = updated[index] with { World = world };
                changed = true;
            }
            if (changed)
            {
                var next = configuration with { Accounts = updated };
                store.Save(next); configuration = next; RenderAccounts();
                Log("Character worlds updated from fresh control-addon heartbeats.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Log("Could not collect character worlds: " + ex.Message); }
    }
    private async void RefreshMaintenance(object? sender, RoutedEventArgs args)
    {
        await maintenance.RefreshAsync(true, shutdown.Token);
        MaintenanceStatus.Text = maintenance.Status;
        RefreshCharacterMaintenance();
        RefreshSessions();
    }
    private void UpdateExistingControlAddon()
    {
        // Update an already-deployed addon through the existing installer;
        // never create an installation merely because the manager opened.
        try
        {
            if (string.IsNullOrWhiteSpace(configuration.WindowerPath)) return;
            var installed = Path.Combine(Path.GetDirectoryName(configuration.WindowerPath)!, "addons", "FFXIManager", "FFXIManager.lua");
            var bundled = AppPayload.FilePath("windower_addons/FFXIManager/FFXIManager.lua");
            if (File.Exists(installed) && File.Exists(bundled) && !File.ReadAllBytes(installed).AsSpan().SequenceEqual(File.ReadAllBytes(bundled)))
                InstallControlAddon(null, new RoutedEventArgs());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { Log("Addon update needs attention: " + ex.Message); }
    }
    private async void InstallControlAddon(object? sender, RoutedEventArgs args)
    {
        if (addonDeploying) return;
        addonDeploying = true;
        var button = sender as Button;
        if (button != null) button.IsEnabled = false;
        try
        {
            if (!platform.CanLaunch || !File.Exists(configuration.WindowerPath)) throw new InvalidOperationException("Save a valid Windows Windower path first.");
            var deployed = await WindowsAddonDeployment.InstallAsync(configuration.WindowerPath, () =>
            {
                AddonDeploymentStatus.Text = "Windower is in a protected folder. Approve the Windows administrator prompt to install the addon. The manager stays open as your normal Windows user.";
                Log("Requesting Windows permission for addon deployment into the protected Windower folder.");
            });
            AddonDeploymentStatus.Text = "FFXIManager installed at " + deployed.AddonDirectory + ". " + "Auto-load enabled in init.txt." + "\nAlready-running games: enter //lua load FFXIManager once in each instance. Future sessions load it automatically. If already loaded, use //lua reload FFXIManager to activate this update.";
            if (deployed.BackupPath != null) AddonDeploymentStatus.Text += "\nPrevious init script backed up at " + deployed.BackupPath;
            AddonCommandPanel.IsVisible = true;
            Log("Control addon deployed with auto-load enabled; existing passwords, token and addon data preserved. Run //lua load FFXIManager once per running game, or reload if already loaded.");
        }
        catch (Exception ex) { AddonDeploymentStatus.Text = "Addon deployment needs attention: " + ex.Message; Log("Could not install control addon: " + ex.Message); }
        finally { addonDeploying = false; if (button != null) button.IsEnabled = true; }
    }
    private async void ShutdownSelected(object? sender, RoutedEventArgs args) => await ShutdownAccounts(selectedAccounts.ToArray());
    private async void ShutdownAll(object? sender, RoutedEventArgs args)
    {
        AttachVerifiedSessions();
        CollectCharacterWorlds();
        await ShutdownAccounts(sessions.Keys.Concat(pending).Distinct().ToArray());
    }
    private async Task ShutdownAccounts(Guid[] ids)
    {
        if (!configurationReadable || !platform.CanLaunch) return;
        if (ids.Length == 0) { Log("No accounts in this shutdown selection."); return; }
        AttachVerifiedSessions();
        CollectCharacterWorlds();
        // Suppress the whole group before any IPC wait or queued launch can continue.
        foreach (var id in ids) { intentionallyStopped.Add(id); if (accountCancellation.TryGetValue(id, out var cancellation)) cancellation.Cancel(); }
        try
        {
            var next = configuration with { Accounts = configuration.ConfiguredAccounts.Select(a => ids.Contains(a.Id) ? a with { MaintainConnection = false } : a).ToList() };
            store.Save(next); configuration = next;
        }
        catch (Exception ex) { Log("Could not save shutdown settings; reconnects suppressed for this manager session. " + ex.Message); return; }
        Log($"Graceful shutdown requested for {ids.Length} account(s). Reconnects disabled for the whole group.");
        RenderAccounts();
        await Task.WhenAll(ids.Select(id => ShutdownAccount(id, policySaved: true)));
    }
    private async void CancelShutdowns(object? sender, RoutedEventArgs args) => await Task.WhenAll(shutdownStops.Keys.ToArray().Select(CancelShutdownAccount));
    private async Task CancelShutdownAccount(Guid id)
    {
        if (!shutdownStops.TryGetValue(id, out var stop) || !shutdownCancelling.Add(id)) return;
        var account = configuration.ConfiguredAccounts.FirstOrDefault(a => a.Id == id);
        try
        {
            var expected = shutdownSessions.GetValueOrDefault(id);
            var finished = shutdownFinished.GetValueOrDefault(id);
            stop.Cancel();
            intentionallyStopped.Add(id);
            if (account?.MaintainConnection == true)
            {
                configuration = configuration with { Accounts = configuration.ConfiguredAccounts.Select(a => a.Id == id ? a with { MaintainConnection = false } : a).ToList() };
                Save();
            }
            if (finished != null) await finished.Task;
            if (account == null) return;
            if (expected == null || !sessions.TryGetValue(id, out var process) || platform.Inspect(process) == SessionHealth.Exited || AddonDataDirectory is not {} data)
            { Log(account.Label + ": shutdown tracking cancelled; no live countdown is available to cancel."); return; }
            var ipc = new ShutdownIpc(data);
            if (ipc.ReadSession(account, DateTimeOffset.UtcNow)?.Nonce != expected.Nonce)
            { Log(account.Label + ": retries and cleanup cancelled. Character already disconnected or session changed; the in-game countdown cannot be cancelled now."); return; }
            await ipc.CancelAsync(account, expected, shutdown.Token);
            Log(account.Label + ": shutdown retries and cleanup cancelled; /shutdown off dispatched to this game. Maintain connection remains off.");
        }
        catch (Exception ex) { Log(account?.Label + ": retries and cleanup cancelled, but in-game cancellation needs attention: " + ex.Message); }
        finally { shutdownCancelling.Remove(id); UpdateSelectionControls(); }
    }
    private async Task ShutdownAccount(Guid id, bool scheduled = false, bool policySaved = false)
    {
        if (shutdownCancelling.Contains(id)) { Log("Wait for this account's cancellation to finish before requesting another shutdown."); return; }
        AttachVerifiedSessions();
        CollectCharacterWorlds();
        if (!scheduled) intentionallyStopped.Add(id);
        if (accountCancellation.TryGetValue(id, out var cancellation)) cancellation.Cancel();
        if (!shuttingDown.Add(id))
        {
            Log(configuration.ConfiguredAccounts.FirstOrDefault(a => a.Id == id)?.Label + ": shutdown is already being tracked. Wait for completion or the retry message; no duplicate command sent.");
            return;
        }
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        shutdownStops[id] = stop;
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        shutdownFinished[id] = finished;
        try
        {
            var account = configuration.ConfiguredAccounts.Single(a => a.Id == id);
            if (!scheduled && !policySaved)
            {
                configuration = configuration with { Accounts = configuration.ConfiguredAccounts.Select(a => a.Id == id ? a with { MaintainConnection = false } : a).ToList() };
                Save();
            }
            if (pending.Contains(id)) { Log(account.Label + ": launch is in progress; maintenance stopped. Inspect the process when launch finishes."); return; }
            if (!sessions.TryGetValue(id, out var process) || platform.Inspect(process) == SessionHealth.Exited) { Log(account.Label + ": no running managed process."); return; }
            var data = Path.Combine(Path.GetDirectoryName(configuration.WindowerPath)!, "addons", "FFXIManager", "data");
            var ipc = new ShutdownIpc(data);
            var game = ipc.ReadSession(account, DateTimeOffset.UtcNow) ?? throw new InvalidOperationException("A fresh character/world heartbeat is required. Load FFXIManager in this game first.");
            if (game.RegisteredUtc < new DateTimeOffset(process.StartedUtc).AddSeconds(-2)) throw new InvalidOperationException("The heartbeat belongs to an older process.");
            shutdownSessions[id] = game;
            shutdownProgress[id] = "sending graceful shutdown";
            RefreshSessions();
            await ipc.RequestAsync(account, game, stop.Token);
            var dispatchedAt = DateTimeOffset.UtcNow;
            Log(account.Label + ": /shutdown dispatched through IPC. Tracking POL until exit; a leftover process is cleaned up five seconds after confirmed character logout.");
            var deadline = dispatchedAt + ShutdownCleanupPolicy.GracefulTimeout;
            var lastDispatchAt = dispatchedAt;
            var retries = 0;
            DateTimeOffset? logoutObservedAt = null;
            var stateReader = new CharacterStateReader(data);
            while (DateTimeOffset.UtcNow < deadline && platform.Inspect(process) != SessionHealth.Exited)
            {
                var now = DateTimeOffset.UtcNow;
                var elapsed = now - dispatchedAt;
                var heartbeat = ipc.ReadSession(account, now);
                if (heartbeat != null && heartbeat.Nonce != game.Nonce)
                    throw new InvalidOperationException("The character session changed during shutdown. No retry or forced cleanup will target the replacement session.");
                var state = stateReader.Read(account, now);
                if (heartbeat == null && state?.Nonce == game.Nonce && state.LoggedOutUtc >= dispatchedAt.AddSeconds(-1))
                    logoutObservedAt ??= state.LoggedOutUtc > now ? now : state.LoggedOutUtc;
                if (ShutdownCleanupPolicy.CanTerminate(true, elapsed, heartbeat != null, logoutObservedAt is {} observed ? now - observed : null)) break;
                if (ShutdownCleanupPolicy.ShouldRetry(heartbeat != null, now - lastDispatchAt, retries))
                {
                    shutdownProgress[id] = $"character still online · retrying graceful shutdown · {retries + 1}/{ShutdownCleanupPolicy.MaximumRetries}";
                    RefreshSessions();
                    Log(account.Label + $": shutdown countdown did not complete; retrying the same live session ({retries + 1}/{ShutdownCleanupPolicy.MaximumRetries}).");
                    await ipc.RequestAsync(account, heartbeat!, stop.Token);
                    retries++;
                    lastDispatchAt = DateTimeOffset.UtcNow;
                    deadline = lastDispatchAt + ShutdownCleanupPolicy.GracefulTimeout;
                    continue;
                }
                if (heartbeat != null && retries >= ShutdownCleanupPolicy.MaximumRetries && now - lastDispatchAt >= ShutdownCleanupPolicy.CountdownAllowance) break;
                shutdownProgress[id] = logoutObservedAt is {} loggedOut
                    ? $"logout confirmed · waiting for POL exit · {Math.Min(5, (int)(now - loggedOut).TotalSeconds)}/5 seconds"
                    : heartbeat == null ? "heartbeat ended · waiting for logout confirmation / POL exit"
                    : $"graceful shutdown requested · character still online · {(int)(now - lastDispatchAt).TotalSeconds} seconds · retries {retries}/{ShutdownCleanupPolicy.MaximumRetries}";
                RefreshSessions();
                await Task.Delay(500, stop.Token);
            }
            if (platform.Inspect(process) == SessionHealth.Exited)
                Log(account.Label + (forcedClosed.Contains(id) ? ": forced cleanup completed." : ": graceful shutdown completed; POL process exited."));
            else if (ShutdownCleanupPolicy.CanTerminate(true, DateTimeOffset.UtcNow - dispatchedAt, ipc.ReadSession(account, DateTimeOffset.UtcNow) != null,
                logoutObservedAt is {} confirmed ? DateTimeOffset.UtcNow - confirmed : null))
            {
                shutdownProgress[id] = "shutdown stalled · terminating original POL process";
                RefreshSessions();
                await WindowsProcessCleanup.TerminateAsync(process, configuration.PolPath, stop.Token);
                forcedClosed.Add(id);
                Log(account.Label + ": hung POL process fully terminated; exit verified. Ready for a clean startup.");
            }
            else Log(account.Label + ": shutdown did not complete and the character is still online. The command may have been cancelled or blocked in game. You can retry Graceful shutdown now, or use Force close explicitly.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log(configuration.ConfiguredAccounts.FirstOrDefault(a => a.Id == id)?.Label + ": graceful shutdown needs attention: " + ex.Message); }
        finally { shutdownProgress.Remove(id); shutdownSessions.Remove(id); shutdownStops.Remove(id); shutdownFinished.Remove(id); shuttingDown.Remove(id); finished.TrySetResult(); RenderAccounts(); }
    }

    private async Task ForceCloseAccount(Guid id)
    {
        if (!configurationReadable || !platform.CanLaunch) return;
        if (!forceClosing.Add(id)) return;
        try
        {
            intentionallyStopped.Add(id);
            if (accountCancellation.TryGetValue(id, out var cancellation)) cancellation.Cancel();
            configuration = configuration with { Accounts = configuration.ConfiguredAccounts.Select(a => a.Id == id ? a with { MaintainConnection = false } : a).ToList() };
            Save();
            if (!sessions.TryGetValue(id, out var process)) throw new InvalidOperationException("No attached process is available for this account.");
            shutdownProgress[id] = "terminating original POL process";
            RefreshSessions();
            await WindowsProcessCleanup.TerminateAsync(process, configuration.PolPath, shutdown.Token);
            forcedClosed.Add(id);
            Log(configuration.ConfiguredAccounts.Single(a => a.Id == id).Label + ": process exit confirmed after forced cleanup. Ready for a clean startup.");
        }
        catch (Exception ex) { Log("Forced cleanup needs attention: " + ex.Message); }
        finally { forceClosing.Remove(id); shutdownProgress.Remove(id); RenderAccounts(); }
    }

    private void DiscoverSessions(object? sender, RoutedEventArgs args)
    {
        AttachVerifiedSessions();
        DiscoveredSessions.Children.Clear();
        if (platform is not WindowsSessionPlatform windows || !windows.CanLaunch)
        { DiscoveredSessions.Children.Add(new TextBlock { Text = "Session discovery is available on Windows." }); return; }
        var running = windows.Discover();
        if (configurationReadable)
        {
            var imported = configuration.ConfiguredAccounts.ToList();
            var importedAny = false;
            var attachments = new Dictionary<Guid, ManagedProcess>();
            foreach (var game in running.Where(g => g.Character != null))
            {
                var matches = imported.Where(a => a.Character.Equals(game.Character, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (running.Count(g => g.Character == game.Character) != 1) continue;
                if (matches.Length == 0)
                {
                    var draft = new AccountConfiguration(Guid.NewGuid(), game.Character!, "", game.Character!, "", "", "");
                    imported.Add(draft);
                    attachments[draft.Id] = game.Process;
                    importedAny = true;
                    Log(game.Character + ": imported running character; complete POL member, profile, and world mapping in its account section.");
                }
                else if (matches.Length == 1 && string.IsNullOrWhiteSpace(matches[0].World)) attachments[matches[0].Id] = game.Process;
            }
            if (importedAny)
            {
                try { var next = configuration with { Accounts = imported }; store.Save(next); configuration = next; foreach (var item in attachments) RememberSession(item.Key, item.Value); RenderAccounts(); }
                catch (Exception ex) { Log("Could not save discovered characters: " + ex.Message); }
            }
            else foreach (var item in attachments) RememberSession(item.Key, item.Value);
        }
        foreach (var game in running)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            row.Children.Add(new TextBlock { Text = $"{game.Character ?? "POL (not logged in)"} · PID {game.Process.Pid}", VerticalAlignment = VerticalAlignment.Center });
            var matches = configuration.ConfiguredAccounts.Where(a => a.Character.Equals(game.Character, StringComparison.OrdinalIgnoreCase)).ToArray();
            // Automatic adoption requires a unique process and a fresh matching world heartbeat.
            if (matches.Length == 1 && running.Count(p => p.Character == game.Character) == 1 && !sessions.ContainsKey(matches[0].Id))
            {
                try
                {
                    var data = Path.Combine(Path.GetDirectoryName(configuration.WindowerPath) ?? "", "addons", "FFXIManager", "data");
                    var heartbeat = new ShutdownIpc(data).ReadSession(matches[0], DateTimeOffset.UtcNow);
                    if (heartbeat != null && heartbeat.RegisteredUtc >= new DateTimeOffset(game.Process.StartedUtc).AddSeconds(-2))
                    { RememberSession(matches[0].Id, game.Process); Log(matches[0].Label + ": attached to running character with matching world heartbeat."); }
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) { }
            }
            var attach = new Button { Content = "Configure / attach", IsEnabled = game.Character != null && configurationReadable };
            attach.Click += async (_, _) =>
            {
                if (windows.Inspect(game.Process) == SessionHealth.Exited) { Log("This process has exited; discover sessions again."); return; }
                var currentMatches = configuration.ConfiguredAccounts.Where(a => a.Character.Equals(game.Character, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (currentMatches.Length > 1) { Log("Multiple configured accounts use this character name. Confirm the world before attaching."); return; }
                var configured = currentMatches.SingleOrDefault();
                if (configured != null && sessions.TryGetValue(configured.Id, out var existing) && windows.Inspect(existing) != SessionHealth.Exited && existing != game.Process)
                { Log("This account is already attached to a running process."); return; }
                var draft = configured ?? new AccountConfiguration(Guid.NewGuid(), game.Character!, "", game.Character!, "", "", "");
                var dialog = new AccountDialog(draft, vault.IsAvailable, vault.Contains(draft.Id), WindowsInstallationDiscovery.ReadProfiles(configuration.WindowerPath));
                var edit = await dialog.ShowDialog<AccountEdit?>(this);
                if (edit == null) return;
                if (!edit.Account.Character.Equals(game.Character, StringComparison.OrdinalIgnoreCase) || windows.Inspect(game.Process) == SessionHealth.Exited)
                { Log("Character identity or process changed; no session attached."); return; }
                try
                {
                    if (edit.Credentials != null)
                    {
                        var prior = vault.Read(edit.Account.Id);
                        vault.Save(edit.Account.Id, new AccountCredentials(string.IsNullOrEmpty(edit.Credentials.PolPassword) ? prior?.PolPassword ?? "" : edit.Credentials.PolPassword,
                            string.IsNullOrEmpty(edit.Credentials.SquareEnixPassword) ? prior?.SquareEnixPassword ?? "" : edit.Credentials.SquareEnixPassword,
                            string.IsNullOrEmpty(edit.Credentials.MemberPassword) ? prior?.MemberPassword ?? "" : edit.Credentials.MemberPassword));
                    }
                    if (edit.DeleteCredentials) vault.Delete(edit.Account.Id);
                    var next = configuration with { Accounts = configuration.ConfiguredAccounts.Where(a => a.Id != edit.Account.Id).Append(edit.Account).ToList(),
                        PolPath = string.IsNullOrWhiteSpace(configuration.PolPath) ? game.ExecutablePath : configuration.PolPath };
                    store.Save(next); configuration = next; PolPath.Text = next.PolPath;
                    RememberSession(edit.Account.Id, game.Process);
                    Log(edit.Account.Label + ": attached by your selection. World verification through the control addon is still required for shutdown.");
                    RenderAccounts();
                }
                catch (Exception ex) { Log("Could not attach session: " + ex.Message); }
            };
            row.Children.Add(attach);
            DiscoveredSessions.Children.Add(row);
        }
        if (running.Count == 0) DiscoveredSessions.Children.Add(new TextBlock { Text = "No accessible POL sessions found." });
        RefreshSessions();
    }
}
