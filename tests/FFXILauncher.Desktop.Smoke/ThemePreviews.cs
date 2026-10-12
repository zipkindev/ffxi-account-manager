using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FFXILauncher.Core;
using FFXILauncher.Desktop;
using System.Net;

internal static class ThemePreviews
{
    public static void Render()
    {
        var root = Path.Combine(Path.GetTempPath(), "ffxi-theme-review-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var output = "artifacts/theme-review";
        Directory.CreateDirectory(output);
        var main = new AccountConfiguration(Guid.NewGuid(), "Main character", "Low Res", "Adventurer", "Asura", "", "EXAMPLE-ID", true, true, ScheduleMaintenance: true);
        var alt = new AccountConfiguration(Guid.NewGuid(), "Second character", "HiRes", "Explorer", "Asura", "", "EXAMPLE-ALT", false, true);
        var data = Path.Combine(root, "addons", "FFXIManager", "data");
        Directory.CreateDirectory(data);
        foreach (var a in new[] { main, alt })
        {
            var time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var report = $"1234567890_abcdef0123456789\n{a.Character}\n{a.World}\n{time-60}\n{time}\n";
            var stem = a.World.ToLowerInvariant()+"_"+a.Character.ToLowerInvariant();
            File.WriteAllText(Path.Combine(data, stem+".status"), report);
            File.WriteAllText(Path.Combine(data, stem+".state"), report+"2540\n3214\n1069\n1074\n0\n272\nDho Gates\n0\n");
            File.WriteAllText(Path.Combine(data, stem+".environment"), report+"272\nRain\n480\nWatersday\nFull Moon\n100\n");
        }
        new ConfigurationStore(root).Save(new LauncherConfiguration(WindowerPath: Path.Combine(root, "Windower.exe"), Accounts: [main,alt], LandscapeCards: false));
        var window = new MainWindow(root, false) { Width = 1200, Height = 1000 };
        window.Show();
        window.FindControl<TextBlock>("Footer")!.Text = "Example accounts · artwork review · no game sessions controlled.";
        window.FindControl<Expander>("LauncherSetup")!.IsExpanded = false;
        foreach (var theme in ThemeCatalog.All)
        {
            window.FindControl<ComboBox>("ThemePicker")!.SelectedItem = theme;
            Dispatcher.UIThread.RunJobs();
            Capture(window, output+"/"+theme.Id+".png");
        }
        window.FindControl<ComboBox>("ThemePicker")!.SelectedItem = ThemeCatalog.Resolve("abyssea");
        window.FindControl<CheckBox>("LandscapeCardsToggle")!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Capture(window, output+"/abyssea-landscape-cards.png");
        window.FindControl<CheckBox>("LandscapeCardsToggle")!.IsChecked = false;
        window.Width = 900;
        Dispatcher.UIThread.RunJobs();
        Capture(window, output+"/abyssea-narrow.png");
        window.Close();
        Sheet(ThemeCatalog.All.Where(t => t.Artwork != null).ToArray(), output, "artwork-sheet.png", 3);
        Sheet(ThemeCatalog.All.Where(t => t.Artwork == null).ToArray(), output, "ui-sheet.png", 2);
        var links = string.Join("\n", ThemeCatalog.All.Select(t => $"<a href='{t.Id}.png' target='_blank'><img src='{t.Id}.png' alt='{WebUtility.HtmlEncode(t.Name)}'><strong>{WebUtility.HtmlEncode(t.Name)}</strong></a>"));
        File.WriteAllText(output+"/index.html", "<!doctype html><meta charset='utf-8'><title>FFXI theme review</title><style>body{background:#101720;color:#e5edf5;font:16px system-ui;margin:32px}main{display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:24px}a{color:inherit;text-decoration:none}img{width:100%;border-radius:10px}strong{display:block;margin:8px 0}</style><h1>FFXI artwork themes</h1><p>Actual app renders with example accounts. Click a theme for full size. Artwork is cropped at its original aspect ratio. Expanded views show a tall left panel and matching right background; character cards use the translucent setting.</p><main>"+links+"</main>");
        Directory.Delete(root, true);
    }
    private static void Capture(Window window, string path)
    {
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Missing theme frame.");
        frame.Save(path, PngBitmapEncoderOptions.Default);
    }
    private static void Sheet(AppTheme[] themes, string output, string name, int columns)
    {
        var rows = (themes.Length+columns-1)/columns;
        var window = new Window { Width=columns*390, Height=rows*360, Background=Brush.Parse("#101720") };
        var grid = new Grid { Margin=new Thickness(8), ColumnDefinitions=new ColumnDefinitions(string.Join(',',Enumerable.Repeat("*",columns))), RowDefinitions=new RowDefinitions(string.Join(',',Enumerable.Repeat("*",rows))) };
        var images = new List<Bitmap>();
        for (var i=0;i<themes.Length;i++)
        {
            var bitmap=new Bitmap(output+"/"+themes[i].Id+".png"); images.Add(bitmap);
            var panel=new StackPanel { Margin=new Thickness(8), Spacing=6 };
            panel.Children.Add(new Image { Source=bitmap, Height=318, Stretch=Stretch.Uniform });
            panel.Children.Add(new TextBlock { Text=themes[i].Name, Foreground=Brush.Parse("#E5EDF5"), FontSize=14, HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Center });
            Grid.SetColumn(panel,i%columns);Grid.SetRow(panel,i/columns);grid.Children.Add(panel);
        }
        window.Content=grid;window.Show();Dispatcher.UIThread.RunJobs();Capture(window,output+"/"+name);window.Close();
        foreach (var image in images) image.Dispose();
    }
}
