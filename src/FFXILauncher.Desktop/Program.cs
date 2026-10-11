using Avalonia;

namespace FFXILauncher.Desktop;
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--install-unattended")
        {
            try { Platform.WindowsUnattendedSetup.InstallAsync().GetAwaiter().GetResult(); }
            catch { Environment.ExitCode = 1; }
            return;
        }
        if (args.Length > 0 && args[0] == Platform.WindowsAddonDeployment.HelperArgument)
        {
            Environment.ExitCode = Platform.WindowsAddonDeployment.RunHelper(args);
            return;
        }
        if (args.Length > 0 && args[0] == Platform.WindowsAutoLogin.HelperArgument)
        {
            Environment.ExitCode = Platform.WindowsAutoLogin.RunWorker(args);
            return;
        }
        if (args.Length > 0 && args[0] == "--calibrate-login")
        {
            Environment.ExitCode = Platform.WindowsAutoLogin.RunCalibration(args);
            return;
        }
        using var instance = new Mutex(true, "FFXILauncher-" + Environment.UserName, out var created);
        if (!created) return;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
