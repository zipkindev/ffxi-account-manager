using System.Diagnostics;
using System.Runtime.InteropServices;
using FFXILauncher.Core;
using FFXILauncher.Desktop.Platform;

internal static class NativePolSizing
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowText(IntPtr h, string text);
    public static void Run()
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Native sizing tests require Windows.");
        var previousDpi = WindowsLoginWindow.SetThreadDpiAwarenessContext(new(-4));
        var h = CreateWindowEx(0, "STATIC", "PlayOnline Viewer - isolated sizing fixture", 0x00CF0000, 20, 20, 1200, 900, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (h == IntPtr.Zero) throw new Exception("Could not create the isolated sizing fixture.");
        try
        {
            ShowWindow(h, 4); // Show without activating or sending input.
            using var own = Process.GetCurrentProcess();
            var target = new ManagedProcess(own.Id, own.StartTime.ToUniversalTime());
            var window = new WindowsLoginWindow(target, Environment.ProcessPath!);
            window.NormalizePolClient();
            if (!GetClientRect(h, out var client) || client.Right - client.Left != 640 || client.Bottom - client.Top != 480) throw new Exception("Native POL normalization did not produce a 640x480 client area.");
            window.NormalizePolClient();
            if (!GetClientRect(h, out client) || client.Right - client.Left != 640 || client.Bottom - client.Top != 480) throw new Exception("Repeated normalization changed client geometry.");
            SetWindowText(h, "Final Fantasy XI");
            SetWindowPos(h, IntPtr.Zero, 0, 0, 900, 650, 0x0002 | 0x0004 | 0x0010);
            GetClientRect(h, out var gameSize);
            own.Refresh();
            window.NormalizePolClient();
            if (!GetClientRect(h, out client) || client.Right != gameSize.Right || client.Bottom != gameSize.Bottom) throw new Exception("Game-window guard changed the fixture geometry.");
            try
            {
                new WindowsLoginWindow(target with { StartedUtc = target.StartedUtc.AddSeconds(1) }, Environment.ProcessPath!).NormalizePolClient();
                throw new Exception("A changed process identity was accepted.");
            }
            catch (LoginSafetyException) { }
            Console.WriteLine("PASS: native POL client normalization, repeated calls, game-window exclusion, and process-identity guard; no login input sent.");
        }
        finally { DestroyWindow(h); if (previousDpi != IntPtr.Zero) WindowsLoginWindow.SetThreadDpiAwarenessContext(previousDpi); }
    }
}
