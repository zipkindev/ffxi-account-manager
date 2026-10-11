using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FFXILauncher.Core;

namespace FFXILauncher.Desktop;
public sealed record AccountEdit(AccountConfiguration Account, AccountCredentials? Credentials, bool DeleteCredentials);
internal sealed record WindowerProfileChoice(string Value, string Label)
{
    public override string ToString() => Label;
}
public sealed class AccountDialog : Window
{
    public AccountDialog(AccountConfiguration? account, bool vaultAvailable, bool credentialsSaved, IReadOnlyList<string>? availableProfiles = null)
    {
        Title = account == null ? "Add existing account" : "Edit account";
        Width = 640; Height = 850; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = ThemeCatalog.BrushFor("Background");
        Foreground = ThemeCatalog.BrushFor("Text");
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(24) };
        var heading = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 16) };
        heading.Children.Add(new TextBlock { Text = Title, FontSize = 24, FontWeight = FontWeight.SemiBold });
        heading.Children.Add(new TextBlock { Text = "Manage a character already registered in POL. Saving here does not create or change a POL member or Square Enix account.", TextWrapping = TextWrapping.Wrap, Foreground = ThemeCatalog.BrushFor("Muted") });
        layout.Children.Add(heading);
        var form = new StackPanel { Spacing = 18 };
        TextBlock Hint(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = ThemeCatalog.BrushFor("Muted") };
        StackPanel Section(string title, string help)
        {
            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.SemiBold });
            panel.Children.Add(Hint(help));
            return panel;
        }
        TextBox Field(StackPanel parent, string name, string caption, string? value, bool secret = false)
        {
            parent.Children.Add(new TextBlock { Text = caption, TextWrapping = TextWrapping.Wrap });
            var input = new TextBox { Name = name, Text = value ?? "", PasswordChar = secret ? '●' : '\0', IsEnabled = !secret || vaultAvailable };
            parent.Children.Add(input);
            return input;
        }
        var identity = Section("Square Enix account", "For automatic login with the POL password already saved: provide your SE ID and SE password. POL member, profile, character and world are reused or detected; enter overrides below only when needed. Complete Windows login validation is required for each account and automation build.");
        var seId = Field(identity, "SquareEnixIdField", "Square Enix ID · required for automatic login", account?.SquareEnixId);
        seId.PlaceholderText = "SE ID needs configuration";
        identity.Children.Add(Hint(credentialsSaved ? "Passwords are saved encrypted. Leave a password blank to retain its saved value." : "Passwords are encrypted for this Windows user. POL's saved passwords are not copied into this app."));
        var se = Field(identity, "SquareEnixPasswordField", "Square Enix password · required for automatic login", "", true);
        form.Children.Add(identity);

        var profileSection = Section("Windower launch profile", "Choose the Windower profile used for this character's next launch. Running sessions keep their current profile.");
        var choices = new List<WindowerProfileChoice> { new("", "Automatic / default profile") };
        foreach (var name in (availableProfiles ?? []).Distinct(StringComparer.OrdinalIgnoreCase)) choices.Add(new(name, name == "Default" ? "Default Profile" : name));
        var selectedProfile = choices.FirstOrDefault(p => p.Value.Equals(account?.Profile ?? "", StringComparison.OrdinalIgnoreCase));
        if (selectedProfile == null)
        {
            selectedProfile = new(account!.Profile, account.Profile + " (not currently detected)");
            choices.Add(selectedProfile);
        }
        var profile = new ComboBox { Name = "ProfileField", ItemsSource = choices, SelectedItem = selectedProfile, HorizontalAlignment = HorizontalAlignment.Stretch };
        profileSection.Children.Add(profile);
        if (availableProfiles?.Count is not > 0) profileSection.Children.Add(Hint("No named profiles detected. Check the Windower path in Launcher setup."));
        form.Children.Add(profileSection);

        var tracking = Section("Character and world", "These can be collected from a running character. Confirm a target when multiple characters are available. A confirmed world is needed for world-specific maintenance and shutdown.");
        var character = Field(tracking, "CharacterField", "Character name · optional until detected or selected", account?.Character);
        tracking.Children.Add(new TextBlock { Text = "World · confirm now or collect from the addon" });
        var world = new ComboBox { Name = "WorldField", ItemsSource = WorldCatalog.Names, SelectedItem = WorldCatalog.Normalize(account?.World), HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "World not confirmed" };
        tracking.Children.Add(world);
        var label = Field(tracking, "LabelField", "Display label · optional", account?.Label);
        form.Children.Add(new Expander { Header = "Character / tracking details", IsExpanded = !string.IsNullOrWhiteSpace(account?.Character), Content = tracking, HorizontalAlignment = HorizontalAlignment.Stretch });

        var launch = Section("POL and Windower mapping overrides", "Use these if automatic mapping is unavailable or ambiguous, including multiple POL members sharing one SE ID. A detected Default or sole Windower profile is used automatically. Otherwise choose a profile override. POL member mapping is pending Windows login validation. Existing POL settings are not changed.");
        var member = Field(launch, "PolMemberField", "POL member name · optional mapping override", account?.PolMember);
        launch.Children.Add(Hint("Use the name shown on POL's member selection screen. It may differ from your character name and SE ID."));
        var polId = Field(launch, "PlayOnlineIdField", "PlayOnline ID · optional service-account reference", account?.PlayOnlineId);
        form.Children.Add(new Expander { Header = "Optional POL / Windower overrides", Content = launch, HorizontalAlignment = HorizontalAlignment.Stretch });

        var credentials = Section("Additional login prompts", "Only configure these if POL does not save its password, your member has a local password, or the account uses a security token.");
        var polSaved = new CheckBox { Name = "PolPasswordSavedOption", Content = "POL already saves the PlayOnline password", IsChecked = account?.PolPasswordStoredInPol ?? true };
        credentials.Children.Add(polSaved);
        var polPanel = new StackPanel { Spacing = 8, IsVisible = polSaved.IsChecked != true };
        var pol = Field(polPanel, "PolPasswordField", "PlayOnline password · required when POL does not save it", "", true);
        credentials.Children.Add(polPanel);
        polSaved.IsCheckedChanged += (_, _) => polPanel.IsVisible = polSaved.IsChecked != true;
        var needsMember = new CheckBox { Name = "MemberPasswordOption", Content = "This POL member has a local member password", IsChecked = account?.MemberPasswordRequired ?? false };
        credentials.Children.Add(needsMember);
        var memberPanel = new StackPanel { Spacing = 8, IsVisible = needsMember.IsChecked == true };
        var memberPassword = Field(memberPanel, "MemberPasswordField", "Local member password · separate from the PlayOnline password", "", true);
        credentials.Children.Add(memberPanel);
        needsMember.IsCheckedChanged += (_, _) => memberPanel.IsVisible = needsMember.IsChecked == true;
        var otp = new CheckBox { Name = "OneTimePasswordOption", Content = "Account uses a security token / one-time password", IsChecked = account?.UsesOneTimePassword ?? false };
        credentials.Children.Add(otp);
        credentials.Children.Add(Hint("A security token requires a fresh code at login. This app does not store a reusable code or support unattended OTP login."));
        var forget = new CheckBox { Name = "DeleteCredentialsOption", Content = "Delete all saved passwords", IsVisible = credentialsSaved };
        credentials.Children.Add(forget);
        form.Children.Add(new Expander { Name = "CredentialsSection", Header = "Conditional passwords / security token", IsExpanded = false, Content = credentials, HorizontalAlignment = HorizontalAlignment.Stretch });

        var scroll = new ScrollViewer { Content = form };
        Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        var footer = new StackPanel { Spacing = 8, Margin = new Thickness(0, 16, 0, 0) };
        var error = new TextBlock { Name = "AccountError", Foreground = Brushes.Salmon, TextWrapping = TextWrapping.Wrap };
        footer.Children.Add(error);
        var save = new Button { Name = "SaveAccountButton", Content = "Save account settings", HorizontalAlignment = HorizontalAlignment.Stretch };
        save.Classes.Add("primary");
        save.Click += (_, _) =>
        {
            var name = character.Text?.Trim() ?? "";
            if (name.Length > 0 && !Regex.IsMatch(name, "^[A-Za-z]+$")) { error.Text = "Character names must contain letters only."; return; }
            if (name.Length == 0 && string.IsNullOrWhiteSpace(seId.Text)) { error.Text = "Enter an SE ID, or a character name to save a tracking-only draft."; return; }
            var hasPol = !string.IsNullOrEmpty(pol.Text);
            var hasSe = !string.IsNullOrEmpty(se.Text);
            var hasMember = !string.IsNullOrEmpty(memberPassword.Text);
            if (forget.IsChecked == true && (hasPol || hasSe || hasMember)) { error.Text = "Choose either new passwords or deletion."; return; }
            var updated = new AccountConfiguration(account?.Id ?? Guid.NewGuid(), string.IsNullOrWhiteSpace(label.Text) ? (name.Length > 0 ? name : seId.Text!.Trim()) : label.Text.Trim(), (profile.SelectedItem as WindowerProfileChoice)?.Value ?? "", name, world.SelectedItem as string ?? "", member.Text?.Trim() ?? "", seId.Text?.Trim() ?? "", account?.MaintainConnection ?? false, account?.AutoLogin ?? false, polId.Text?.Trim().ToUpperInvariant() ?? "", account?.ScheduleMaintenance ?? false, polSaved.IsChecked == true, needsMember.IsChecked == true, otp.IsChecked == true);
            var passwords = (hasPol || hasSe || hasMember) && vaultAvailable ? new AccountCredentials(pol.Text!, se.Text!, memberPassword.Text!) : null;
            pol.Text = ""; se.Text = ""; memberPassword.Text = "";
            Close(new AccountEdit(updated, passwords, forget.IsChecked == true));
        };
        footer.Children.Add(save);
        Grid.SetRow(footer, 2); layout.Children.Add(footer);
        Closed += (_, _) => { pol.Text = ""; se.Text = ""; memberPassword.Text = ""; };
        Content = layout;
    }
}
