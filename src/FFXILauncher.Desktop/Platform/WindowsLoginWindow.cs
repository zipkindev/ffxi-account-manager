using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;
using FFXILauncher.Core;

namespace FFXILauncher.Desktop.Platform;

// All input is foreground-only, bound to the process creation time and configured image.
internal sealed class WindowsLoginWindow(ManagedProcess target, string executable)
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Keyboard { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public Keyboard Keyboard; [FieldOffset(0)] public Mouse Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct Mouse { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Value; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int command);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref Point p);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr h, uint flag);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] input, int size);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint key, uint type);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern short VkKeyScan(char character);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr source, int sx, int sy, uint flags);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] pixels, byte[] info, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll")] internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    private IntPtr Handle(bool allowMissing = false)
    {
        using var p = Process.GetProcessById(target.Pid);
        if (p.HasExited || p.StartTime.ToUniversalTime() != target.StartedUtc || !string.Equals(WindowsSessionPlatform.ExecutablePath(p.Id), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase))
            throw new LoginSafetyException("The target POL process changed. Input stopped.");
        var h = p.MainWindowHandle;
        if (h == IntPtr.Zero && allowMissing) return IntPtr.Zero;
        GetWindowThreadProcessId(h, out var pid);
        if (h == IntPtr.Zero) throw new LoginWindowUnavailableException("The target POL window is unavailable.");
        if (pid != target.Pid) throw new LoginSafetyException("The target POL window belongs to another process. Input stopped.");
        return h;
    }
    public async Task WaitForWindowAsync(CancellationToken cancellation)
    {
        // Process discovery can precede POL's first window. Wait without sending input,
        // while continuing to validate the original process identity on every poll.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        var previous = IntPtr.Zero;
        var stableSince = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellation.ThrowIfCancellationRequested();
            var h = Handle(allowMissing: true);
            var ready = h != IntPtr.Zero && GetClientRect(h, out var r) &&
                r.Right >= 320 && r.Bottom >= 240 && r.Right <= 8192 && r.Bottom <= 8192;
            if (!ready || previous != h) stableSince = DateTimeOffset.UtcNow;
            if (ready && previous == h && DateTimeOffset.UtcNow - stableSince >= TimeSpan.FromMilliseconds(500)) return;
            previous = ready ? h : IntPtr.Zero;
            await Task.Delay(100, cancellation);
        }
        throw new LoginSafetyException("The owned POL process did not open a usable window within 30 seconds. No login input was sent.");
    }
    public async Task WaitForGameWindowAsync(CancellationToken cancellation)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        var previous = IntPtr.Zero;
        var stableSince = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellation.ThrowIfCancellationRequested();
            var h = Handle(allowMissing: true);
            using var p = Process.GetProcessById(target.Pid);
            var ready = h != IntPtr.Zero && p.MainWindowTitle.Trim().Equals("Final Fantasy XI", StringComparison.OrdinalIgnoreCase);
            if (!ready || previous != h) stableSince = DateTimeOffset.UtcNow;
            if (ready && previous == h && DateTimeOffset.UtcNow - stableSince >= TimeSpan.FromMilliseconds(200)) return;
            previous = ready ? h : IntPtr.Zero;
            await Task.Delay(50, cancellation);
        }
        throw new LoginSafetyException("The owned FFXI window did not finish opening within 30 seconds.");
    }
    public bool IsGameWindow
    {
        get
        {
            // POL briefly has no main HWND while creating FFXI. Classification
            // is a readiness query, not input permission. Preserve process/image
            // validation but let the capture loop wait for its replacement window.
            if (Handle(allowMissing: true) == IntPtr.Zero) return false;
            using var p = Process.GetProcessById(target.Pid);
            return p.MainWindowTitle.Trim().Equals("Final Fantasy XI", StringComparison.OrdinalIgnoreCase);
        }
    }
    public void NormalizePolClient()
    {
        var h = Handle();
        using var owned = Process.GetProcessById(target.Pid);
        if (!owned.MainWindowTitle.StartsWith("PlayOnline Viewer", StringComparison.OrdinalIgnoreCase)) return;
        if (!GetClientRect(h, out var client) || !GetWindowRect(h, out var outer)) throw new LoginSafetyException("POL window bounds are unavailable.");
        if (client.Right - client.Left == 640 && client.Bottom - client.Top == 480) return;
        var width = 640 + outer.Right - outer.Left - (client.Right - client.Left);
        var height = 480 + outer.Bottom - outer.Top - (client.Bottom - client.Top);
        // Retain position, Z order and focus; only this owned POL client changes.
        if (!SetWindowPos(h, IntPtr.Zero, 0, 0, width, height, 0x0002 | 0x0004 | 0x0010))
            throw new LoginSafetyException("Windows could not resize POL. Disable native POL sizing to retain the current window.");
    }
    public void Focus()
    {
        var h = Handle(); ShowWindow(h, 9);
        if (GetForegroundWindow() != h)
        {
            // A balanced ALT press allows Windows to honor a user-initiated foreground request.
            Send([KeyInput(0x12, false), KeyInput(0x12, true)]);
            SetForegroundWindow(h);
        }
        var deadline = DateTime.UtcNow.AddSeconds(2);
        // POL can replace its main HWND while opening FFXI. Revalidate the
        // current owned handle while waiting; do not repeatedly steal focus
        // or keep waiting for a window that this process has already replaced.
        while (GetForegroundWindow() != Handle() && DateTime.UtcNow < deadline) Thread.Sleep(20);
        RequireForeground();
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder text, int count);
    public bool IsCharacterWindow(string character)
    {
        var text = new StringBuilder(256);
        GetWindowText(Handle(), text, text.Capacity);
        return text.ToString().Trim().Equals(character, StringComparison.OrdinalIgnoreCase);
    }
    private (IntPtr H, Point Origin, int Width, int Height) RequireForeground()
    {
        var h = Handle();
        if (GetForegroundWindow() != h) throw new LoginFocusException("POL lost keyboard focus. Input stopped; bring the target window forward and retry.");
        if (!GetClientRect(h, out var r) || r.Right < 320 || r.Bottom < 240 || r.Right > 8192 || r.Bottom > 8192) throw new LoginSafetyException("Unsupported POL client geometry.");
        var origin = new Point();
        if (!ClientToScreen(h, ref origin)) throw new LoginSafetyException("Cannot measure the POL client.");
        return (h, origin, r.Right, r.Bottom);
    }
    private static Input KeyInput(ushort key, bool up, bool extended = false) => new() { Type = 1, Value = new() { Keyboard = new() { Scan = (ushort)MapVirtualKey(key, 0), Flags = 8u | (up ? 2u : 0u) | (extended || key is >= 0x21 and <= 0x28 ? 1u : 0u) } } };
    private static void Send(Input[] inputs)
    {
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length) throw new LoginSafetyException("Windows rejected login input. Check the helper privilege level.");
    }
    public void Key(ushort key) { RequireForeground(); Send([KeyInput(key, false)]); try { Thread.Sleep(80); } finally { Send([KeyInput(key, true)]); } }
    public void ParkCursor()
    {
        // Park the mouse away from menus so hover cannot override the keyboard cursor.
        var g = RequireForeground();
        var point = new Point { X = g.Origin.X + g.Width / 2, Y = g.Origin.Y + g.Height / 4 };
        if (GetAncestor(WindowFromPoint(point), 2) != g.H || !SetCursorPos(point.X, point.Y)) throw new LoginSafetyException("Cannot position the mouse safely inside the managed game window.");
        Thread.Sleep(40);
        RequireForeground();
    }
    public void ToggleWindowerConsole()
    {
        RequireForeground();
        Send([KeyInput(0x2d, false, extended: true)]);
        try { Thread.Sleep(80); }
        finally { Send([KeyInput(0x2d, true, extended: true)]); }
    }
    public void ConfirmGameSelection(int pressMilliseconds = 40)
    {
        if (pressMilliseconds is < 1 or > 80) throw new LoginSafetyException("Game key duration is outside the bounded input range.");
        ParkCursor();
        // Use the extended Return scan code for the game's numeric Enter confirm key.
        Send([KeyInput(0x0d, false, extended: true)]);
        try { Thread.Sleep(pressMilliseconds); }
        finally { Send([KeyInput(0x0d, true, extended: true)]); }
    }
    public void ReturnToGameMenu(string character, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        Handle();
        using var owned = Process.GetProcessById(target.Pid);
        if (string.IsNullOrWhiteSpace(character) || !owned.MainWindowTitle.Trim().Equals(character, StringComparison.OrdinalIgnoreCase))
            throw new LoginSafetyException("Menu return requires the verified owned character window.");
        // Close any open chat editor, then issue only this fixed game command.
        // Normal logout returns to FFXI menus and keeps POL authenticated.
        Key(0x1b);
        Password("/logout", cancellation);
        cancellation.ThrowIfCancellationRequested();
        Key(0x0d);
    }
    public bool ReplayViewportMatches(int width, int height)
    {
        var g = RequireForeground();
        foreach (var offset in new[] { new Point { X = 5, Y = 5 }, new Point { X = g.Width / 2, Y = g.Height / 2 }, new Point { X = g.Width - 5, Y = g.Height - 5 } })
            if (GetAncestor(WindowFromPoint(new() { X = g.Origin.X + offset.X, Y = g.Origin.Y + offset.Y }), 2) != g.H)
                throw new LoginObscuredException("FFXI is obscured. Cached input stopped.");
        return IsGameWindow && g.Width == width && g.Height == height;
    }
    public void Password(string password, CancellationToken cancellation)
    {
        if (password.Length is < 1 or > 128 || password.Any(c => c < 32 || c > 126 || VkKeyScan(c) < 0)) throw new LoginSafetyException("The password cannot be entered with the active Windows keyboard layout.");
        foreach (var c in password)
        {
            cancellation.ThrowIfCancellationRequested(); RequireForeground();
            var scan = VkKeyScan(c); var key = (ushort)(scan & 255); var shift = (scan >> 8) & 255;
            var modifiers = new List<ushort>(); if ((shift & 1) != 0) modifiers.Add(0x10); if ((shift & 2) != 0) modifiers.Add(0x11); if ((shift & 4) != 0) modifiers.Add(0x12);
            try { foreach (var modifier in modifiers) Send([KeyInput(modifier, false)]); Key(key); }
            finally { foreach (var modifier in modifiers.AsEnumerable().Reverse()) Send([KeyInput(modifier, true)]); }
            Thread.Sleep(40);
        }
    }
    public void Click(double x, double y, int frameWidth, int frameHeight, int pressMilliseconds = 80)
    {
        if (pressMilliseconds is < 1 or > 80) throw new LoginSafetyException("Click duration is outside the bounded input range.");
        var g = RequireForeground();
        if (g.Width != frameWidth || g.Height != frameHeight || x < 0 || y < 0 || x >= g.Width || y >= g.Height) throw new LoginSafetyException("Window geometry changed. Click cancelled.");
        var point = new Point { X = g.Origin.X + (int)x, Y = g.Origin.Y + (int)y };
        if (GetAncestor(WindowFromPoint(point), 2) != g.H) throw new LoginSafetyException("Another window covers the login control. Click cancelled.");
        SetCursorPos(point.X, point.Y); RequireForeground();
        Send([new() { Value = new() { Mouse = new() { Flags = 2 } } }]);
        try { Thread.Sleep(pressMilliseconds); }
        finally { Send([new() { Value = new() { Mouse = new() { Flags = 4 } } }]); }
    }
    public void Capture(string path)
    {
        var g = RequireForeground();
        foreach (var offset in new[] { new Point { X = 5, Y = 5 }, new Point { X = g.Width / 2, Y = g.Height / 2 }, new Point { X = g.Width - 5, Y = g.Height - 5 } })
            if (GetAncestor(WindowFromPoint(new() { X = g.Origin.X + offset.X, Y = g.Origin.Y + offset.Y }), 2) != g.H) throw new LoginObscuredException("POL is obscured. Capture stopped.");
        // POL/FFXI render with DirectX: the window DC can be entirely black.
        // Read the visible desktop client rectangle after foreground/occlusion checks.
        var dc = GetDC(IntPtr.Zero); var memory = CreateCompatibleDC(dc); var bitmap = CreateCompatibleBitmap(dc, g.Width, g.Height); var old = SelectObject(memory, bitmap);
        try
        {
            if (dc == IntPtr.Zero || memory == IntPtr.Zero || bitmap == IntPtr.Zero || !BitBlt(memory, 0, 0, g.Width, g.Height, dc, g.Origin.X, g.Origin.Y, 0x00CC0020)) throw new LoginSafetyException("POL capture failed.");
            SelectObject(memory, old); old = IntPtr.Zero;
            var info = new byte[40]; BitConverter.GetBytes(40).CopyTo(info, 0); BitConverter.GetBytes(g.Width).CopyTo(info, 4); BitConverter.GetBytes(-g.Height).CopyTo(info, 8); BitConverter.GetBytes((ushort)1).CopyTo(info, 12); BitConverter.GetBytes((ushort)32).CopyTo(info, 14);
            var pixels = new byte[checked(g.Width * g.Height * 4)];
            if (GetDIBits(dc, bitmap, 0, (uint)g.Height, pixels, info, 0) != g.Height) throw new LoginSafetyException("POL capture pixels unavailable.");
            using var file = new BinaryWriter(File.Create(path)); file.Write((ushort)0x4d42); file.Write(54 + pixels.Length); file.Write(0); file.Write(54); file.Write(info); file.Write(pixels);
        }
        finally { if (old != IntPtr.Zero) SelectObject(memory, old); DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(IntPtr.Zero, dc); }
    }
}
