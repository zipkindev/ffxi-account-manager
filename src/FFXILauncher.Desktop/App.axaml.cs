using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace FFXILauncher.Desktop;
public sealed partial class App : Application
{
    public override void Initialize() { AvaloniaXamlLoader.Load(this); ThemeCatalog.Apply(ThemeCatalog.All[0]); }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
