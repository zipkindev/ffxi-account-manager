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
        new ConfigurationStore(root).Save(new LauncherConfiguration(Accounts: [main, alt], Theme: "abyssea"));
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
