using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FFXILauncher.Core;
using FFXILauncher.Desktop;

internal static class ReadmeImages
{
    public static void Render()
    {
        var root = Path.Combine(Path.GetTempPath(), "ffxi-readme-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var output = "docs/images";
        Directory.CreateDirectory(output);
        var main = new AccountConfiguration(Guid.NewGuid(), "Main character", "Low Res", "Adventurer", "Asura", "", "EXAMPLE-ID", true, true, ScheduleMaintenance: true);
        var alt = new AccountConfiguration(Guid.NewGuid(), "Second character", "HiRes", "Explorer", "Asura", "", "EXAMPLE-ALT", false, true);
        var windower = Path.Combine(root, "Windower.exe");
        var data = Path.Combine(root, "addons", "FFXIManager", "data");
        Directory.CreateDirectory(data);
        var now = DateTimeOffset.UtcNow;
        foreach (var account in new[] { main, alt })
        {
            var stem = "asura_" + account.Character.ToLowerInvariant();
            var prefix = $"abcdef0123456789\n{account.Character}\nAsura\n{now.AddMinutes(-2).ToUnixTimeSeconds()}\n{now.ToUnixTimeSeconds()}\n";
            File.WriteAllText(Path.Combine(data, stem + ".status"), prefix);
            File.WriteAllText(Path.Combine(data, stem + ".state"), prefix + "2181\n2181\n1876\n1876\n0\n272\nDho Gates\n0\n");
            File.WriteAllText(Path.Combine(data, stem + ".jobs"), prefix + "45\nGEO\n99\nWHM\n58\n");
            File.WriteAllText(Path.Combine(data, stem + ".points"), prefix + "55999\n56000\n75\n75\n29999\n500\n123456\n1500000\n");
            File.WriteAllText(Path.Combine(data, stem + ".environment"), prefix + "272\nSunshine\n1184\nFiresday\nWaxing Crescent\n25\n");
        }
        new ConfigurationStore(root).Save(new LauncherConfiguration(WindowerPath: windower, Accounts: [main, alt], Theme: "abyssea"));
        var window = new MainWindow(root, false);
        window.Show();
        window.FindControl<TextBlock>("Footer")!.Text = "Example accounts · screenshots show the app UI; no game sessions are controlled.";
        window.FindControl<Expander>("LauncherSetup")!.IsExpanded = false;
        Dispatcher.UIThread.RunJobs();
        Save(window, output + "/dashboard.png");
        window.FindControl<ComboBox>("ThemePicker")!.SelectedItem = ThemeCatalog.Resolve("midnight");
        window.FindControl<Expander>("BulkActions")!.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        Save(window, output + "/bulk-actions.png");
        window.FindControl<Expander>("BulkActions")!.IsExpanded = false;
        window.Height = 1250;
        window.FindControl<Border>("NotificationPanel")!.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        var scroll = window.FindControl<ScrollViewer>("AccountScroll")!;
        scroll.Offset = new Avalonia.Vector(scroll.Offset.X, Math.Max(0, scroll.Offset.Y - 40));
        Dispatcher.UIThread.RunJobs();
        Save(window, output + "/notifications.png");
        var editor = new AccountDialog(main, false, false, ["Default Profile", "Low Res", "HiRes", "UltraHigh"]);
        _ = editor.ShowDialog<AccountEdit?>(window);
        Dispatcher.UIThread.RunJobs();
        Save(editor, output + "/account-setup.png");
        editor.Close();
        window.Close();
        Directory.Delete(root, true);
    }
    private static void Save(Window window, string path)
    {
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Missing screenshot frame.");
        frame.Save(path, PngBitmapEncoderOptions.Default);
    }
}
