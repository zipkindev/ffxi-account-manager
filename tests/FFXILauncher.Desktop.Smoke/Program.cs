using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using FFXILauncher.Desktop.Platform;
using Avalonia.Threading;
using FFXILauncher.Core;
using FFXILauncher.Desktop;

if (args.Contains("--pol-sizing-test")) { NativePolSizing.Run(); return; }
MaintenanceMonitorRegression.Run();
FocusRecoveryRegression.Run();
if (!WindowsLoginEngine.StopsAtCharacterSelection("continue", false, false) ||
    WindowsLoginEngine.StopsAtCharacterSelection("continue", true, false) ||
    !WindowsLoginEngine.StopsAtCharacterSelection("character", false, true) ||
    WindowsLoginEngine.StopsAtCharacterSelection("character", false, false))
    throw new Exception("Disconnect recovery must enter the world while explicit section/cancel trials retain their stop points.");
WindowsAutoLogin.ValidateRecoveryCompletion(true, true, false);
WindowsAutoLogin.ValidateRecoveryCompletion(true, false, true);
WindowsAutoLogin.ValidateRecoveryCompletion(false, false, false);
foreach (var flags in new[] { (false, false), (true, true) })
{
    try { WindowsAutoLogin.ValidateRecoveryCompletion(true, flags.Item1, flags.Item2); throw new Exception("Unverified or conflicting recovery result reported success."); }
    catch (LoginSafetyException) { }
}
Console.WriteLine("PASS: in-place recovery continues beyond character selection; incomplete/conflicting outcomes cannot report verified success.");
if (args.Contains("--maintenance-regression")) return;

if (args is ["--game-window-ready", var processId, var startedTicks, var executable])
{
    var target = new WindowsLoginWindow(new ManagedProcess(int.Parse(processId), new DateTime(long.Parse(startedTicks), DateTimeKind.Utc)), executable);
    target.WaitForGameWindowAsync(CancellationToken.None).GetAwaiter().GetResult();
    Console.WriteLine("PASS: exact existing FFXI process has a stable game window; no input sent.");
    return;
}

{
    var profilesRoot = Path.Combine(Path.GetTempPath(), "ffxi-profiles-" + Guid.NewGuid());
    Directory.CreateDirectory(profilesRoot);
    try
    {
        var launcher = Path.Combine(profilesRoot, "Windower.exe");
        File.WriteAllText(Path.Combine(profilesRoot, "settings.xml"), "<settings><profile name=\"\"/><profile name=\"HiRes\"/><profile name=\"Low Res\"/></settings>");
        if (WindowsInstallationDiscovery.ResolveProfile(launcher, "") != "" || WindowsInstallationDiscovery.ResolveProfile(launcher, "Default") != "")
            throw new Exception("Unnamed Windower default profile was not resolved.");
        if (!WindowsInstallationDiscovery.ReadProfiles(launcher).SequenceEqual(new[] { "Default", "HiRes", "Low Res" })) throw new Exception("Default profile omitted from selection.");
        if (WindowsInstallationDiscovery.ResolveProfile(launcher, "hires") != "HiRes" || WindowsInstallationDiscovery.ResolveProfile(launcher, "Missing") != null)
            throw new Exception("Explicit profile resolution did not validate the configured name.");
        File.WriteAllText(Path.Combine(profilesRoot, "settings.xml"), "<settings><profile name=\"A\"/><profile name=\"B\"/></settings>");
        if (WindowsInstallationDiscovery.ResolveProfile(launcher, "") != null) throw new Exception("Ambiguous profile silently selected.");
        Console.WriteLine("PASS: unnamed default profile resolves to Windower's empty argument; named overrides are checked and ambiguous defaults remain blocked.");
    }
    finally { Directory.Delete(profilesRoot, true); }
    var now = DateTimeOffset.UtcNow;
    var token = new string('a', 64);
    var request = new CalibrationRequest(token, Guid.NewGuid().ToString("N"), Guid.NewGuid(), new ManagedProcess(1, DateTime.UtcNow), "inspect", now.AddSeconds(30));
    if (!WindowsCalibrationControl.Accepts(request, token, now)) throw new Exception("Valid bounded inspection request rejected.");
    if (!WindowsCalibrationControl.Accepts(request with { Operation = "launch", Process = null! }, token, now)) throw new Exception("Bounded configured-account launch rejected.");
    if (!WindowsCalibrationControl.Accepts(request with { Operation = "train-member", PressMs = 1, PollMs = 1 }, token, now)) throw new Exception("Bounded fast section trial rejected.");
    if (!WindowsCalibrationControl.Accepts(request with { Operation = "train-character", PressMs = 1, PollMs = 1 }, token, now)) throw new Exception("Bounded character timing trial rejected.");
    foreach (var gap in new[] { 0, 50, 3000 })
        if (!WindowsCalibrationControl.Accepts(request with { Operation = "train-character-success", PressMs = 40, PollMs = 10, MenuGapMs = gap }, token, now))
            throw new Exception("Bounded successful-route trial rejected.");
    foreach (var gap in new[] { -1, 3001 })
        if (WindowsCalibrationControl.Accepts(request with { Operation = "train-character-success", MenuGapMs = gap }, token, now))
            throw new Exception("Unbounded successful-route trial accepted.");
    if (WindowsCalibrationControl.Accepts(request with { MenuGapMs = 50 }, token, now)) throw new Exception("Menu gap accepted outside success training.");
    foreach (var operation in new[] { "train-password", "train-play", "train-character", "continue-login", "return-game-menu" })
    {
        if (!WindowsCalibrationControl.Accepts(request with { Operation = operation }, token, now)) throw new Exception("Bounded persistent-session section rejected.");
        if (operation != "train-character" && WindowsCalibrationControl.Accepts(request with { Operation = operation, PressMs = 1 }, token, now)) throw new Exception("Unsupported section timing override accepted.");
    }
    foreach (var operation in new[] { "close", "shutdown", "shutdown_all", "train-member" })
    {
        if (!WindowsCalibrationControl.Accepts(request with { Operation = operation }, token, now)) throw new Exception("Bounded account operation rejected.");
        if (WindowsCalibrationControl.Accepts(request with { Operation = operation, Process = null! }, token, now)) throw new Exception("Process-free shutdown or cleanup accepted.");
    }
    foreach (var invalid in new[] { request with { Token = new string('b', 64) }, request with { Nonce = "../outside" }, request with { Operation = "shell" },
        request with { ExpiresUtc = now }, request with { ExpiresUtc = now.AddMinutes(2) }, request with { Account = Guid.Empty }, request with { Process = null! },
        request with { PressMs = 1 }, request with { Operation = "train-member", PressMs = 0 }, request with { Operation = "train-member", PollMs = 51 } })
        if (WindowsCalibrationControl.Accepts(invalid, token, now)) throw new Exception("Invalid calibration request accepted.");
    Console.WriteLine("PASS: calibration rejects foreign capabilities, unsafe operations, invalid identities, and stale/future requests before input.");
    var flowInputs = new[] { new KeyValuePair<string, string>("login.cs", "first\r\nsecond"), new KeyValuePair<string, string>("ocr.ps1", "read") };
    var sameInputs = new[] { new KeyValuePair<string, string>("ocr.ps1", "read"), new KeyValuePair<string, string>("login.cs", "first\nsecond") };
    if (LoginFlowCompatibility.Fingerprint(flowInputs) != LoginFlowCompatibility.Fingerprint(sameInputs) ||
        LoginFlowCompatibility.Fingerprint(flowInputs) == LoginFlowCompatibility.Fingerprint(sameInputs.Append(new("extra.cs", "input"))) ||
        LoginFlowCompatibility.Fingerprint(flowInputs) == LoginFlowCompatibility.Fingerprint(sameInputs.Select(x => x.Key == "login.cs" ? new KeyValuePair<string, string>(x.Key, "changed") : x)) ||
        LoginFlowCompatibility.Current.Length != 64)
        throw new Exception("Login-source fingerprint must be deterministic across ordering/newlines and reject changed or added inputs.");
    if (OperatingSystem.IsWindows())
    {
        var root = Path.Combine(Path.GetTempPath(), "ffxi-validation-" + Guid.NewGuid());
        var account = new AccountConfiguration(Guid.NewGuid(), "Synthetic", "Default", "Synthetic", "Asura", "", "SyntheticID");
        try
        {
            if (WindowsLoginValidation.IsValidated(root, account)) throw new Exception("Unvalidated account accepted.");
            using (var login = new WindowsAutoLogin(root))
            {
                if (WindowsUnattendedSetup.IsElevated && login.SetupProblem(account) != null) throw new Exception("Configured manual login must be available before full-flow validation.");
                if (login.ReadinessProblem(account) == null) throw new Exception("Unattended recovery still requires complete validation.");
                if (login.SetupProblem(account with { UsesOneTimePassword = true }) == null) throw new Exception("Manual automatic login cannot bypass interactive OTP requirements.");
            }
            WindowsLoginValidation.Record(root, account);
            if (!WindowsLoginValidation.IsValidated(root, account)) throw new Exception("Completed validation did not persist.");
            foreach (var changed in new[] { account with { SquareEnixId = "OtherID" }, account with { Character = "Other" }, account with { World = "Odin" }, account with { Profile = "Other" }, account with { UsesOneTimePassword = true }, account with { Id = Guid.NewGuid() }, account with { PolPasswordStoredInPol = false }, account with { MemberPasswordRequired = true } })
                if (WindowsLoginValidation.IsValidated(root, changed) || WindowsLoginValidation.HasPriorValidation(root, changed)) throw new Exception("Changed account mapping reused login validation.");
            var proofPath = Path.Combine(root, "login-validation", account.Id.ToString("N") + ".json");
            var proof = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(proofPath))!;
            proof["Build"] = Guid.NewGuid().ToString("N");
            File.WriteAllText(proofPath, proof.ToJsonString());
            if (!WindowsLoginValidation.IsValidated(root, account))
                throw new Exception("An unrelated build change invalidated unchanged login automation.");
            proof["LoginFlow"] = "E12AB56C4CD3CB2612585B619F2F3D8420F9412FEE653290A0BAD7F0D71A4527";
            File.WriteAllText(proofPath, proof.ToJsonString());
            if (!WindowsLoginValidation.IsValidated(root, account)) throw new Exception("Reviewed unchanged normal login route lost validation after adding the updater branch.");
            proof["LoginFlow"] = new string('0', 64);
            File.WriteAllText(proofPath, proof.ToJsonString());
            if (WindowsLoginValidation.IsValidated(root, account) || !WindowsLoginValidation.HasPriorValidation(root, account))
                throw new Exception("Changed login automation must require validation while preserving the confirmed-disconnect prior-proof route.");
            proof.AsObject().Remove("LoginFlow");
            File.WriteAllText(proofPath, proof.ToJsonString());
            if (WindowsLoginValidation.IsValidated(root, account)) throw new Exception("Unknown legacy build bypassed validation.");
            foreach (var legacy in new[] { "eaa4e971ee0140869898d88ea901d5ad", "cf5fbeb42c504f57bbf3da94d0186a1c", "b8d01e8893f7413e92677bda379cd5d3" })
            {
                proof["Build"] = legacy;
                File.WriteAllText(proofPath, proof.ToJsonString());
                if (!WindowsLoginValidation.IsValidated(root, account) || WindowsLoginValidation.IsValidated(root, account with { Character = "Other" }))
                    throw new Exception("Reviewed legacy flow must retain validation only for the exact account mapping.");
                proof["LoginFlow"] = "mismatched-flow";
                File.WriteAllText(proofPath, proof.ToJsonString());
                if (WindowsLoginValidation.IsValidated(root, account)) throw new Exception("Legacy bridge overrode an explicit incompatible flow.");
                proof.AsObject().Remove("LoginFlow");
            }
            proof["VerifiedUtc"] = DateTimeOffset.UtcNow.AddDays(1).ToString("o");
            File.WriteAllText(proofPath, proof.ToJsonString());
            if (WindowsLoginValidation.IsValidated(root, account) || WindowsLoginValidation.HasPriorValidation(root, account)) throw new Exception("Future validation accepted.");
            File.WriteAllText(Path.Combine(root, "login-validation", account.Id.ToString("N") + ".json"), "{}");
            if (WindowsLoginValidation.IsValidated(root, account) || WindowsLoginValidation.HasPriorValidation(root, account)) throw new Exception("Corrupt proof accepted.");
            Console.WriteLine("PASS: login-source validation survives unrelated builds; changed flows/mappings, unknown legacy builds, future dates and corrupt proofs stay blocked.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}

if (args.Contains("--login-safety"))
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
    var controlRoot = Path.Combine(Path.GetTempPath(), "ffxi-control-test-" + Guid.NewGuid());
    try
    {
        using (var control = new WindowsCalibrationControl(controlRoot, (_, _) => Task.FromResult(new CalibrationOutcome(true, "finished"))))
        {
            var controlDirectory = Path.Combine(controlRoot, "login-control");
            var capability = File.ReadAllText(Path.Combine(controlDirectory, "token"));
            var nonce = Guid.NewGuid().ToString("N");
            WindowsAutoLogin.WriteJson(Path.Combine(controlDirectory, "request.json"), new CalibrationRequest(capability, nonce, Guid.NewGuid(), new ManagedProcess(1, DateTime.UtcNow), "inspect", DateTimeOffset.UtcNow.AddSeconds(30)));
            var deadline = DateTime.UtcNow.AddSeconds(5);
            CalibrationReply? reply;
            do
            {
                reply = WindowsAutoLogin.ReadJson<CalibrationReply>(Path.Combine(controlDirectory, "reply.json"));
                if (reply?.Nonce == nonce && reply.Finished) break;
                Thread.Sleep(25);
            } while (DateTime.UtcNow < deadline);
            if (reply?.Finished != true || !reply.Success) throw new Exception("Bounded diagnostic completion was not delivered.");
            control.Report("Late progress must not overwrite completion.");
            reply = WindowsAutoLogin.ReadJson<CalibrationReply>(Path.Combine(controlDirectory, "reply.json"));
            if (reply?.Finished != true || reply.Message != "finished") throw new Exception("Late progress overwrote a completed reply.");
        }
        Console.WriteLine("PASS: authenticated diagnostic completion remains final after late progress; stub handler sent no input.");
    }
    finally { if (Directory.Exists(controlRoot)) Directory.Delete(controlRoot, true); }
    var replyDirectory = Path.Combine(Path.GetTempPath(), "ffxi-reply-test-" + Guid.NewGuid());
    Directory.CreateDirectory(replyDirectory);
    try
    {
        var replyPath = Path.Combine(replyDirectory, "reply.json");
        WindowsAutoLogin.WriteJson(replyPath, new { State = "old" });
        Task writer;
        using (var reader = File.Open(replyPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            writer = Task.Run(() => WindowsAutoLogin.WriteJson(replyPath, new { State = "finished" }));
            Thread.Sleep(100);
        }
        writer.GetAwaiter().GetResult();
        if (!File.ReadAllText(replyPath).Contains("finished", StringComparison.Ordinal) || Directory.GetFiles(replyDirectory, "*.tmp").Length != 0)
            throw new Exception("Completion reply was dropped or temporary files remained after a transient read lock.");
        Console.WriteLine("PASS: completion reply survives Windows reader locking and temporary files are cleaned up.");
    }
    finally { Directory.Delete(replyDirectory, true); }
    var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
    var start = new System.Diagnostics.ProcessStartInfo(powershell) { UseShellExecute = false, CreateNoWindow = true };
    start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-Command"); start.ArgumentList.Add("Start-Sleep -Seconds 60");
    using var disposable = System.Diagnostics.Process.Start(start)!;
    try
    {
        var correct = new ManagedProcess(disposable.Id, disposable.StartTime.ToUniversalTime());
        var wrongTime = new WindowsLoginWindow(correct with { StartedUtc = correct.StartedUtc.AddSeconds(1) }, powershell);
        var wrongImage = new WindowsLoginWindow(correct, Path.Combine(Path.GetTempPath(), "pol.exe"));
        var hiddenWindow = new WindowsLoginWindow(correct, powershell);
        var temporary = Path.Combine(Path.GetTempPath(), "ffxi-rejected-" + Guid.NewGuid() + ".bmp");
        foreach (var target in new[] { wrongTime, wrongImage, hiddenWindow })
        {
            try { target.Key(0x0d); throw new Exception("Input accepted for an invalid target."); } catch (InvalidOperationException) { }
            try { target.Capture(temporary); throw new Exception("Capture accepted for an invalid target."); } catch (InvalidOperationException) { }
            try { target.ConfirmGameSelection(); throw new Exception("Game confirmation accepted an invalid target."); } catch (InvalidOperationException) { }
            try { target.ReturnToGameMenu("Minibubble", CancellationToken.None); throw new Exception("Menu return accepted an invalid target."); } catch (InvalidOperationException) { }
            try { target.ReplayViewportMatches(1280, 800); throw new Exception("Cached replay accepted an invalid target."); } catch (InvalidOperationException) { }
            try { target.IsCharacterWindow("Synthetic-Test-Only"); throw new Exception("Character-window acknowledgement accepted an invalid target."); } catch (InvalidOperationException) { }
            if (ReferenceEquals(target, hiddenWindow))
            {
                if (target.IsGameWindow) throw new Exception("Missing main window was classified as a ready game window.");
            }
            else
                try { _ = target.IsGameWindow; throw new Exception("Game-window classification accepted a replaced process."); } catch (InvalidOperationException) { }
            if (File.Exists(temporary)) throw new Exception("Invalid target produced a screenshot.");
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { hiddenWindow.Password("Synthetic-Test-Only", cancelled.Token); throw new Exception("Cancelled password accepted."); } catch (OperationCanceledException) { }
        try { hiddenWindow.ReturnToGameMenu("Minibubble", cancelled.Token); throw new Exception("Cancelled menu return accepted."); } catch (OperationCanceledException) { }
        foreach (var duration in new[] { 0, 81 })
            try { hiddenWindow.ConfirmGameSelection(duration); throw new Exception("Unbounded game key duration accepted."); } catch (InvalidOperationException) { }
        try { hiddenWindow.WaitForGameWindowAsync(cancelled.Token).GetAwaiter().GetResult(); throw new Exception("Cancelled transition accepted."); } catch (OperationCanceledException) { }
        try { hiddenWindow.WaitForWindowAsync(cancelled.Token).GetAwaiter().GetResult(); throw new Exception("Cancelled startup wait accepted."); } catch (OperationCanceledException) { }
        foreach (var target in new[] { wrongTime, wrongImage })
        {
            try { target.WaitForGameWindowAsync(CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("Transition accepted a replaced process."); } catch (InvalidOperationException) { }
            try { target.WaitForWindowAsync(CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("Startup wait accepted a replaced process."); } catch (InvalidOperationException) { }
        }
        if (disposable.HasExited) throw new Exception("Input safety checks modified the test process.");
        Console.WriteLine("PASS: input and capture rejected wrong creation time, wrong executable, unavailable window, and cancellation; no keys or credentials sent.");
        var delayedStart = new System.Diagnostics.ProcessStartInfo(powershell) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-STA", "-Command", "Start-Sleep -Seconds 2; Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.Form; $f.Text = 'FFXI isolated startup test'; $f.ClientSize = New-Object System.Drawing.Size(640,480); $f.ShowDialog() | Out-Null" }) delayedStart.ArgumentList.Add(arg);
        using (var delayed = System.Diagnostics.Process.Start(delayedStart)!)
        {
            try
            {
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                var target = new WindowsLoginWindow(new ManagedProcess(delayed.Id, delayed.StartTime.ToUniversalTime()), powershell);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                target.WaitForWindowAsync(timeout.Token).GetAwaiter().GetResult();
                if (elapsed.Elapsed < TimeSpan.FromSeconds(2) || delayed.HasExited) throw new Exception("Startup accepted a process before its delayed window opened.");
                Console.WriteLine("PASS: startup waits for a delayed owned window to become usable without sending input.");
            }
            finally { if (!delayed.HasExited) { delayed.Kill(); delayed.WaitForExit(10000); } }
        }
        if (WindowsAutoLogin.RunWorker([WindowsAutoLogin.HelperArgument, "../outside", "0", "0", "invalid"]) == 0) throw new Exception("Invalid helper capability accepted.");
        Console.WriteLine("PASS: invalid worker session rejected before configuration or credentials are read.");
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        foreach (var expired in new[] { false, true })
        {
            var session = Guid.NewGuid().ToString("N");
            var sessionDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FFXILauncher", "automation", session);
            WindowsPrivateDirectory.Create(sessionDirectory);
            var token = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            File.WriteAllText(Path.Combine(sessionDirectory, "token"), token);
            WindowsAutoLogin.WriteJson(Path.Combine(sessionDirectory, "request.json"), new LoginRequest(expired ? token : new string('0', 64), Guid.NewGuid().ToString("N"), Guid.NewGuid(), correct, expired ? DateTimeOffset.UtcNow.AddSeconds(-1) : DateTimeOffset.UtcNow.AddMinutes(1)));
            var helperArguments = new[] { WindowsAutoLogin.HelperArgument, session, current.Id.ToString(), current.StartTime.ToUniversalTime().Ticks.ToString(), identity.User!.Value };
            if (WindowsAutoLogin.RunWorker(withLastTicks()) != 4) throw new Exception("Worker accepted a replacement parent process.");
            string[] withLastTicks() { var bad = (string[])helperArguments.Clone(); bad[3] = (current.StartTime.ToUniversalTime().Ticks + 1).ToString(); return bad; }
            using var revoke = new System.Threading.Timer(_ => File.Delete(Path.Combine(sessionDirectory, "token")), null, 400, Timeout.Infinite);
            if (WindowsAutoLogin.RunWorker(helperArguments) != 0 || Directory.Exists(sessionDirectory)) throw new Exception("Worker did not exit and clean up its private session after revocation.");
        }
        Console.WriteLine("PASS: changed parent identity, foreign tokens, expired requests, revocation, and private session cleanup; no real account reads or input.");
    }
    finally { if (!disposable.HasExited) { disposable.Kill(); disposable.WaitForExit(10000); } }
    return;
}

if (args.Contains("--maintenance-live"))
{
    var cache = Path.Combine(Path.GetTempPath(), "ffxi-maintenance-" + Guid.NewGuid());
    try
    {
        using var monitor = new MaintenanceMonitor(cache) { Worlds = ["Asura", "Odin"] };
        monitor.RefreshAsync(true, CancellationToken.None).GetAwaiter().GetResult();
        if (monitor.Snapshot.CheckedUtc == null) throw new Exception(monitor.Status);
        using var http = new HttpClient();
        var example = http.GetStringAsync("https://www.playonline.com/ff11us/polnews/news27775.shtml").GetAwaiter().GetResult();
        var parsed = MaintenanceNoticeParser.Parse("https://www.playonline.com/ff11us/polnews/news27775.shtml", "All Worlds Maintenance", example);
        if (parsed?.StartUtc != new DateTimeOffset(2026, 9, 29, 5, 0, 0, TimeSpan.Zero) || parsed.EndUtc != new DateTimeOffset(2026, 9, 29, 6, 30, 0, TimeSpan.Zero)) throw new Exception("User-provided official example did not parse correctly.");
        Console.WriteLine("PASS: supplied official notice parsed to its UTC start and end.");
        Console.WriteLine($"PASS: live official maintenance feed; {monitor.Snapshot.Windows.Count} cached window(s), {monitor.Snapshot.ReviewRequired?.Count ?? 0} requiring review.");
    }
    finally { if (Directory.Exists(cache)) Directory.Delete(cache, true); }
    return;
}

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
if (args.Contains("--theme-previews")) { ThemePreviews.Render(); return; }
if (args.Contains("--readme-images")) { ReadmeImages.Render(); return; }
if (args.Contains("--art-concepts")) { ArtworkConcepts.Render(); return; }
var directory = Path.Combine(Path.GetTempPath(), "ffxi-ui-" + Guid.NewGuid());
var window = new MainWindow(directory, false);
if (window.Icon == null) throw new Exception("The main window must load its bundled application icon.");
window.Show();
Dispatcher.UIThread.RunJobs();
if (window.FindControl<TextBlock>("EmptyState")?.IsVisible != true) throw new Exception("Empty account state did not render.");
if (!OperatingSystem.IsWindows() && window.FindControl<Button>("LaunchEnabledButton")?.IsEnabled != false) throw new Exception("Unsupported platform launch must be disabled.");
Directory.CreateDirectory("artifacts");
using (var frame = window.CaptureRenderedFrame() ?? throw new Exception("No rendered frame")) frame.Save("artifacts/dashboard.png", PngBitmapEncoderOptions.Default);
Console.WriteLine("PASS: dashboard renders and launch is gated by platform.");
var editor = new AccountDialog(null, false, false);
var editorResult = editor.ShowDialog<AccountEdit?>(window);
Dispatcher.UIThread.RunJobs();
using (var frame = editor.CaptureRenderedFrame() ?? throw new Exception("No account editor frame")) frame.Save("artifacts/account-editor.png", PngBitmapEncoderOptions.Default);
Console.WriteLine("PASS: account editor renders.");
foreach (var expander in editor.GetLogicalDescendants().OfType<Expander>().ToArray()) expander.IsExpanded = true;
Dispatcher.UIThread.RunJobs();
var fields = editor.GetLogicalDescendants().OfType<TextBox>().ToArray();
var passwords = fields.Where(f => f.Name is "PolPasswordField" or "SquareEnixPasswordField" or "MemberPasswordField").ToArray();
if (passwords.Length != 3 || passwords.Any(f => f.IsEnabled)) throw new Exception("All credential fields must be disabled without a vault.");
if (editor.GetLogicalDescendants().OfType<ComboBox>().Single(c => c.Name == "WorldField").Items.Count != 16) throw new Exception("World selector must contain the 16 retail worlds without duplicates.");
fields.Single(f => f.Name == "SquareEnixIdField").Text = "TestSE";
editor.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "SaveAccountButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Dispatcher.UIThread.RunJobs();
var savedDraft = editorResult.GetAwaiter().GetResult() ?? throw new Exception("SE-ID-only draft could not be saved.");
if (savedDraft.Account.Profile != "" || savedDraft.Account.Character != "" || savedDraft.Account.World != "" || savedDraft.Account.Label != "TestSE") throw new Exception("Tracking fields must not be mandatory for registering an SE account in the manager.");
var minimal = new AccountDialog(null, true, false);
var minimalResult = minimal.ShowDialog<AccountEdit?>(window);
Dispatcher.UIThread.RunJobs();
minimal.GetLogicalDescendants().OfType<TextBox>().Single(f => f.Name == "SquareEnixIdField").Text = "TestSE";
minimal.GetLogicalDescendants().OfType<TextBox>().Single(f => f.Name == "SquareEnixPasswordField").Text = "synthetic-SE-password";
minimal.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "SaveAccountButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Dispatcher.UIThread.RunJobs();
var savedMinimal = minimalResult.GetAwaiter().GetResult() ?? throw new Exception("Minimum login configuration was rejected.");
if (savedMinimal.Credentials?.SquareEnixPassword != "synthetic-SE-password" || !savedMinimal.Account.PolPasswordStoredInPol || savedMinimal.Account.PolMember != "") throw new Exception("SE ID and SE password must be sufficient to save login configuration with POL's password already saved.");
Console.WriteLine("PASS: SE ID/password minimum setup and optional tracking/mapping fields.");
var profileAccount = new AccountConfiguration(Guid.NewGuid(), "Profile test", "HiRes", "Synthetic", "Asura", "", "SyntheticSE");
var profileEditor = new AccountDialog(profileAccount, false, false, ["Default", "HiRes", "Low Res", "UltraHigh"]);
var profileResult = profileEditor.ShowDialog<AccountEdit?>(window);
Dispatcher.UIThread.RunJobs();
var profilePicker = profileEditor.GetLogicalDescendants().OfType<ComboBox>().Single(c => c.Name == "ProfileField");
if ((profilePicker.SelectedItem as WindowerProfileChoice)?.Value != "HiRes") throw new Exception("Existing profile selection was not retained.");
profilePicker.SelectedItem = profilePicker.Items.OfType<WindowerProfileChoice>().Single(p => p.Value == "Low Res");
profileEditor.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "SaveAccountButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Dispatcher.UIThread.RunJobs();
if (profileResult.GetAwaiter().GetResult()?.Account.Profile != "Low Res") throw new Exception("Named profile with spaces was not saved exactly.");
Console.WriteLine("PASS: per-account detected profile picker preserves existing choices and saves Low Res exactly.");

window.Close();
var mainAccount = new AccountConfiguration(Guid.NewGuid(), "Main account", "Main", "Hauplibond", "Odin", "", "", true);
var secondAccount = new AccountConfiguration(Guid.NewGuid(), "Second account", "Second", "Otherchar", "Asura", "", "KnownSE", true);
var playerData = Path.Combine(directory, "addons", "FFXIManager", "data");
Directory.CreateDirectory(playerData);
var playerTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
var playerNonce = "1234567890_abcdef0123456789";
var playerPayload = $"{playerNonce}\nHauplibond\nOdin\n{playerTime - 60}\n{playerTime}\n";
var playerStatePayload = playerPayload + "0\n1500\n200\n500\n1\n100\nWest Ronfaure\n";
File.WriteAllText(Path.Combine(playerData, "odin_hauplibond.status"), playerPayload);
File.WriteAllText(Path.Combine(playerData, "odin_hauplibond.state"), playerStatePayload + "0\n");
File.WriteAllText(Path.Combine(playerData, "odin_hauplibond.jobs"), playerPayload + "46\nDRK\n99\nWAR\n57\n");
File.WriteAllText(Path.Combine(playerData, "odin_hauplibond.environment"), playerPayload + "100\nRain\n1184\nFiresday\nWaxing Crescent\n25\n");
new ConfigurationStore(directory).Save(new(Path.Combine(directory, "Windower.exe"), "", [mainAccount, secondAccount]));
var scheduleTime = DateTimeOffset.UtcNow;
var scheduleFixtures = new MaintenanceSnapshot([
    new("https://www.playonline.com/ff11us/polnews/news100.shtml", "Fixture global maintenance", scheduleTime.AddHours(3), scheduleTime.AddHours(4)),
    new("https://www.playonline.com/ff11us/polnews/news101.shtml", "Fixture Odin maintenance", scheduleTime.AddHours(1), scheduleTime.AddHours(2), "Odin"),
    new("https://www.playonline.com/ff11us/polnews/news102.shtml", "Fixture Asura maintenance", scheduleTime.AddHours(2), scheduleTime.AddHours(3), "Asura")
], scheduleTime);
File.WriteAllText(Path.Combine(directory, "maintenance.json"), System.Text.Json.JsonSerializer.Serialize(scheduleFixtures));
// An external-terminal pause must remain visible after a fresh manager opens,
// and only explicitly enabling Maintain connection clears it.
var externalDirectory = Path.Combine(Path.GetTempPath(), "ffxi-external-ui-" + Guid.NewGuid().ToString("N"));
var externalStore = new ConfigurationStore(externalDirectory);
externalStore.Save(new LauncherConfiguration(Accounts: [mainAccount.PauseForExternalSession(), secondAccount]));
var externalWindow = new MainWindow(externalDirectory, false);
externalWindow.Show();
Dispatcher.UIThread.RunJobs();
var externalStatus = externalWindow.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "SessionStatus_" + mainAccount.Id.ToString("N"));
if (externalStatus.Text != AccountConfiguration.ExternalSessionReason)
    throw new Exception("The external-login error and reason must remain visible on a fresh idle account card.");
var externalMaintain = externalWindow.GetLogicalDescendants().OfType<ToggleSwitch>().Where(t => Equals(t.Content, "Maintain connection")).First();
if (externalMaintain.IsChecked != false) throw new Exception("External-terminal pause must display Maintain connection Off.");
var manualPause = (string?)typeof(MainWindow).GetMethod("ManualLoginProblem", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(externalWindow, [mainAccount.PauseForExternalSession()]);
using (var pausedLogin = new WindowsAutoLogin(externalDirectory))
    if (manualPause == AccountConfiguration.ExternalSessionReason || pausedLogin.ReadinessProblem(mainAccount.PauseForExternalSession()) != AccountConfiguration.ExternalSessionReason || WindowsAutoLogin.ExternalPauseProblem(mainAccount.PauseForExternalSession(), true) != null || WindowsAutoLogin.ExternalPauseProblem(mainAccount.PauseForExternalSession(), false) != AccountConfiguration.ExternalSessionReason)
        throw new Exception("Explicit manual login must override the pause while automatic login stays blocked and Maintain connection stays off.");
var confirmation = typeof(MainWindow).GetMethod("ConfirmExternalSessionLaunch", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
foreach (var accept in new[] { false, true })
{
    var answer = (Task<bool>)confirmation.Invoke(externalWindow, [mainAccount.PauseForExternalSession()])!;
    Dispatcher.UIThread.RunJobs();
    var warning = externalWindow.OwnedWindows.Single(w => w.Title == "Logged in from another terminal");
    if (!warning.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text!.Contains("POL-1039") && t.Text.Contains("Maintain connection will stay off")))
        throw new Exception("Manual override must explain the external-login code and unchanged recovery switch.");
    warning.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, accept ? "Launch anyway" : "Cancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Dispatcher.UIThread.RunJobs();
    if (answer.GetAwaiter().GetResult() != accept || externalStore.Load().ConfiguredAccounts.Single(a => a.Id == mainAccount.Id).MaintainConnection)
        throw new Exception("Manual confirmation must support cancel/override without enabling Maintain connection.");
}
Console.WriteLine("PASS: POL-1039 manual warning supports Cancel and Launch anyway while automatic recovery remains off; no game launched.");
externalMaintain.IsChecked = true;
Dispatcher.UIThread.RunJobs();
var resumedExternal = externalStore.Load().ConfiguredAccounts.Single(a => a.Id == mainAccount.Id);
if (!resumedExternal.MaintainConnection || resumedExternal.RecoveryPauseReason != null || externalStatus.Text == AccountConfiguration.ExternalSessionReason)
    throw new Exception("Explicitly re-enabling Maintain connection must clear the saved external-login reason and resume policy.");
externalWindow.Close();
Directory.Delete(externalDirectory, true);
Console.WriteLine("PASS: external-login pause survives manager restart, shows error/reason and clears only on explicit Maintain connection enable.");
var configured = new MainWindow(directory, false);
configured.Show();
Dispatcher.UIThread.RunJobs();
foreach (var account in new[] { mainAccount, secondAccount })
{
    var key = account.Id.ToString("N");
    if (configured.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "SessionStatus_" + key).Text != "No process tracked" ||
        configured.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "Shutdown_" + key).IsEnabled ||
        configured.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "ForceClose_" + key).IsEnabled)
        throw new Exception("Idle cards must show No process tracked with both shutdown actions disabled.");
}
if (configured.GetLogicalDescendants().OfType<Button>().Any(b => Equals(b.Content, "Validate login")))
    throw new Exception("Obsolete Validate login control is still displayed.");
if (configured.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "Launch_" + mainAccount.Id.ToString("N")).Content?.ToString() != "Launch profile")
    throw new Exception("Account launch control is missing.");
string PlayerText() => configured.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "CharacterState_" + mainAccount.Id.ToString("N")).Text!;
if (!PlayerText().StartsWith("ML 46 99DRK/57WAR\n")) throw new Exception("Job summary missing from character card.");
if (!PlayerText().Contains("Current · West Ronfaure · Dead · HP 0/1500 · MP 200/500")) throw new Exception("Current player stats missing from card.");
var weatherPanel = configured.GetLogicalDescendants().OfType<StackPanel>().Single(p => p.Name == "Weather_" + mainAccount.Id.ToString("N"));
if (!weatherPanel.Children.OfType<TextBlock>().Any(t => t.Text == "Rain · Water ×1 (single)") || weatherPanel.Children.OfType<Image>().Count() != 1) throw new Exception("Single Rain must show one game weather icon and its element.");
var versionButton = configured.FindControl<Button>("VersionButton")!;
if (versionButton.Content?.ToString() != "v" + AppRelease.Version + " · What’s new") throw new Exception("Displayed version does not match the assembly.");
versionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Dispatcher.UIThread.RunJobs();
var releaseDialog = configured.OwnedWindows.Single(w => w.Title == "FFXI Account Manager · v" + AppRelease.Version);
if (!releaseDialog.GetLogicalDescendants().OfType<SelectableTextBlock>().Any(t => t.Text!.Contains("## " + AppRelease.Version))) throw new Exception("Version click did not display bundled current release notes.");
releaseDialog.Close();
Dispatcher.UIThread.RunJobs();
configured.FindControl<Button>("LicensesButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Dispatcher.UIThread.RunJobs();
var licenseDialog = configured.OwnedWindows.Single(w => w.Title == "Licenses & credits");
var licenseText = licenseDialog.GetLogicalDescendants().OfType<SelectableTextBlock>().Single().Text!;
if (!licenseText.Contains("MIT License") || !licenseText.Contains("zipkindev") || !licenseText.Contains("does not license third-party images") || !licenseText.Contains("Avalonia-LICENSE.txt"))
    throw new Exception("Offline licenses view is missing code, artwork scope or dependency notices.");
licenseDialog.Close();
Dispatcher.UIThread.RunJobs();
if (PlayerText().Contains("Vana’diel:") || configured.FindControl<TextBlock>("VanaClockStatus")!.Text?.Contains("Moon:") != true) throw new Exception("Time and moon must appear once in the shared header.");
File.Delete(Path.Combine(playerData, "odin_hauplibond.status"));
File.WriteAllText(Path.Combine(playerData, "odin_hauplibond.state"), playerStatePayload + playerTime + "\n");
typeof(MainWindow).GetMethod("RefreshSessions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(configured, null);
if (!PlayerText().Contains("Last logout · West Ronfaure · Dead at logout")) throw new Exception("Dead logout state missing from card after heartbeat removal.");
Console.WriteLine("PASS: cards show current player vitals and preserve dead logout zone/stats after heartbeat removal.");
var statusFile = Path.Combine(playerData, "odin_hauplibond.status");
var stateFile = Path.Combine(playerData, "odin_hauplibond.state");
var savedLogoutState = File.ReadAllText(stateFile);
var newPlayerPayload = playerPayload.Replace(playerPayload.Split('\n')[0], new string('b', 32));
File.WriteAllText(statusFile, newPlayerPayload);
typeof(MainWindow).GetMethod("RefreshSessions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(configured, null);
if (PlayerText() != "Current · Alive · player stats pending") throw new Exception("A fresh session must not inherit the old session's dead logout state.");
File.WriteAllText(statusFile, playerPayload);
File.WriteAllText(stateFile, playerStatePayload.Replace("100\nWest Ronfaure", "280\nMog Garden") + "0\n");
typeof(MainWindow).GetMethod("RefreshSessions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(configured, null);
if (!PlayerText().Contains("Current · Mog Garden · Alive · HP/MP pending") || PlayerText().Contains("Dead"))
    throw new Exception("Legacy Mog Garden death must be suppressed without inventing vitals.");
File.Delete(statusFile);
File.WriteAllText(stateFile, savedLogoutState);
typeof(MainWindow).GetMethod("RefreshSessions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(configured, null);
Console.WriteLine("PASS: new session defaults alive without stale death, and Mog Garden suppresses legacy false death.");
var toggles = configured.GetLogicalDescendants().OfType<ToggleSwitch>().ToArray();
if (toggles.Length != 6) throw new Exception("Auto login and maintenance require separate controls.");
var autoToggle = toggles.First(t => Equals(t.Content, "Auto login"));
var toggle = toggles.First(t => Equals(t.Content, "Maintain connection"));
toggle.IsChecked = false;
Dispatcher.UIThread.RunJobs();
if (new ConfigurationStore(directory).Load().ConfiguredAccounts.Single(a => a.Id == mainAccount.Id).MaintainConnection) throw new Exception("Maintenance toggle was not persisted.");
autoToggle.IsChecked = false;
Dispatcher.UIThread.RunJobs();
if (new ConfigurationStore(directory).Load().ConfiguredAccounts.Single(a => a.Id == mainAccount.Id).AutoLoginEnabled) throw new Exception("Disabling auto login must disable maintenance.");
toggle.IsChecked = true;
Dispatcher.UIThread.RunJobs();
if (!new ConfigurationStore(directory).Load().ConfiguredAccounts.Single(a => a.Id == mainAccount.Id).AutoLoginEnabled) throw new Exception("Maintenance must include auto login.");
toggles.First(t => Equals(t.Content, "Schedule server maintenance")).IsChecked = true;
Dispatcher.UIThread.RunJobs();
if (!new ConfigurationStore(directory).Load().ConfiguredAccounts.Single(a => a.Id == mainAccount.Id).ScheduleMaintenance) throw new Exception("Scheduled maintenance must persist.");
if (configured.FindControl<Button>("StartEnabledButton")!.IsEnabled) throw new Exception("Unimplemented automatic login must be visibly disabled.");
if (configured.FindControl<Button>("StartEnabledButton")!.Content?.ToString()?.Contains("pending", StringComparison.OrdinalIgnoreCase) == true) throw new Exception("Disabled auto login must explain its cause rather than show generic pending.");
var identities = configured.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
if (!identities.Any(t => t.Contains("Hauplibond · SE ID needs configuration")) || !identities.Any(t => t.Contains("Otherchar · SE ID: KnownSE"))) throw new Exception("Account identity must display its SE ID or an explicit missing-configuration message.");
configured.GetLogicalDescendants().OfType<CheckBox>().Single(c => c.Name == "SelectAccount_" + mainAccount.Id.ToString("N")).IsChecked = true;
Dispatcher.UIThread.RunJobs();
if (configured.FindControl<TextBlock>("SelectionSummary")!.Text != "1 selected") throw new Exception("Selection must remain separate from enabled automation policies.");
configured.FindControl<ComboBox>("BulkPolicy")!.SelectedIndex = 3;
configured.FindControl<Button>("ApplyBulkPolicyButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Dispatcher.UIThread.RunJobs();
var savedBulk = new ConfigurationStore(directory).Load().ConfiguredAccounts;
if (savedBulk.Single(a => a.Id == mainAccount.Id).MaintainConnection || !savedBulk.Single(a => a.Id == mainAccount.Id).AutoLoginEnabled || !savedBulk.Single(a => a.Id == secondAccount.Id).MaintainConnection) throw new Exception("Bulk policy must affect only selected accounts and preserve auto login when disabling maintaining.");
if (configured.FindControl<TextBlock>("SelectionSummary")!.Text != "1 selected" || configured.FindControl<CheckBox>("SelectAllAccounts")!.IsChecked != null) throw new Exception("Selection must survive a dashboard redraw.");
if (OperatingSystem.IsWindows())
{
    configured.FindControl<ComboBox>("BulkPolicy")!.SelectedIndex = 2;
    configured.FindControl<Button>("ApplyBulkPolicyButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Dispatcher.UIThread.RunJobs();
    configured.FindControl<Button>("ShutdownSelectedButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Dispatcher.UIThread.RunJobs();
    var stopped = new ConfigurationStore(directory).Load().ConfiguredAccounts;
    if (stopped.Single(a => a.Id == mainAccount.Id).MaintainConnection || !stopped.Single(a => a.Id == secondAccount.Id).MaintainConnection) throw new Exception("Selected graceful shutdown must suppress only selected accounts.");
}
configured.FindControl<CheckBox>("SelectAllAccounts")!.IsChecked = true;
Dispatcher.UIThread.RunJobs();
if (configured.FindControl<TextBlock>("SelectionSummary")!.Text != "2 selected") throw new Exception("Select all must include every configured account.");
configured.FindControl<ComboBox>("BulkPolicy")!.SelectedIndex = 1;
configured.FindControl<Button>("ApplyBulkPolicyButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Dispatcher.UIThread.RunJobs();
if (new ConfigurationStore(directory).Load().ConfiguredAccounts.Any(a => a.AutoLoginEnabled || a.MaintainConnection)) throw new Exception("Bulk disable must clear auto login and maintain for the entire selection.");
var scheduleLabels = configured.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.Name?.StartsWith("CharacterMaintenance_", StringComparison.Ordinal) == true).ToArray();
var mainSchedule = scheduleLabels.Single(t => t.Name == "CharacterMaintenance_" + mainAccount.Id.ToString("N")).Text!;
var secondSchedule = scheduleLabels.Single(t => t.Name == "CharacterMaintenance_" + secondAccount.Id.ToString("N")).Text!;
if (!mainSchedule.StartsWith("Odin: Upcoming") || mainSchedule.Contains("Global:") || mainSchedule.Contains("Asura:")) throw new Exception("Odin card must show only its imminent matching-world notice.");
if (!secondSchedule.StartsWith("Asura: Upcoming") || secondSchedule.Contains("Odin:")) throw new Exception("Asura card must show only its imminent matching-world notice.");
if (mainSchedule.Contains("Pacific time:") || mainSchedule.Split('\n').Length != 1) throw new Exception("Maintenance must be compact and use only local times.");
configured.FindControl<CheckBox>("ShowCardMaintenanceToggle")!.IsChecked = false;
Dispatcher.UIThread.RunJobs();
if (scheduleLabels.Any(t => t.Parent is Border panel && panel.IsVisible) || !configured.FindControl<Border>("MainMaintenancePanel")!.IsVisible || new ConfigurationStore(directory).Load().ShowCardMaintenance) throw new Exception("Hiding card alerts must persist and leave the main maintenance section visible.");
var fullSchedule = configured.FindControl<TextBlock>("MaintenanceStatus")!.Text!;
if (!fullSchedule.Contains("Global:") || !fullSchedule.Contains("Odin:") || !fullSchedule.Contains("Asura:")) throw new Exception("The main maintenance panel must retain the full schedule when card alerts are hidden.");
configured.FindControl<CheckBox>("ShowCardMaintenanceToggle")!.IsChecked = true;
Dispatcher.UIThread.RunJobs();
var settingsPanel = configured.FindControl<Border>("MainMaintenancePanel")!;
if (!settingsPanel.GetLogicalDescendants().Contains(configured.FindControl<CheckBox>("ShowCardMaintenanceToggle")!)) throw new Exception("Card display option must live in Server maintenance.");
var alertHours = configured.FindControl<NumericUpDown>("CardMaintenanceHoursInput")!;
var displayWarningPath = Path.Combine(playerData, "odin_hauplibond.alert");
File.WriteAllText(displayWarningPath, playerPayload + "incoming-017-system\nEmergency maintenance will begin soon.\n");
var duplicateWarningPaths = new[] { "Minibubble", "Nanakia" }.Select(name => Path.Combine(playerData, "odin_" + name.ToLowerInvariant() + ".alert")).ToArray();
foreach (var warningPath in duplicateWarningPaths)
{
    var character = Path.GetFileNameWithoutExtension(warningPath)[5..];
    File.WriteAllText(warningPath, playerPayload.Replace("Hauplibond", character) + "incoming-017-system\nEmergency maintenance will begin soon.\n");
}
alertHours.Value = 6;
Dispatcher.UIThread.RunJobs();
if (scheduleLabels.Any(t => t.Text!.Contains("In-game") || t.Text!.Contains("Emergency maintenance") || t.Text!.Contains("reconnect paused")))
    throw new Exception("Tracked in-game messages and holds must not be repeated on character cards.");
var sharedNotices = configured.FindControl<TextBlock>("MaintenanceStatus")!.Text!;
if (sharedNotices.Contains("reconnect paused")) throw new Exception("Upcoming tracked warnings cannot claim reconnect is already paused.");
if (sharedNotices.Split("Emergency maintenance will begin soon.").Length != 2)
    throw new Exception("Identical warnings observed by three characters must appear once in the shared maintenance section.");
if (InGameMaintenance.Read(playerData, "Odin", DateTimeOffset.UtcNow).Count != 3 ||
    InGameMaintenance.Hold(InGameMaintenance.Read(playerData, "Odin", DateTimeOffset.UtcNow), scheduleFixtures, DateTimeOffset.UtcNow) != null)
    throw new Exception("Upcoming notices must retain all tracked sources without blocking login early.");
foreach (var warningPath in duplicateWarningPaths) File.Delete(warningPath);
alertHours.Value = 0;
Dispatcher.UIThread.RunJobs();
if (scheduleLabels.Any(t => t.Parent is Border panel && panel.IsVisible)) throw new Exception("The global zero-hour preference must hide upcoming notices on all cards.");
if (new ConfigurationStore(directory).Load().CardMaintenanceHours != 0) throw new Exception("Global alert timing did not persist.");
if (!configured.FindControl<TextBlock>("MaintenanceStatus")!.Text!.Contains("Emergency maintenance will begin soon.")) throw new Exception("In-game warnings must remain in the main section when the card threshold is not met.");
File.Delete(displayWarningPath);
alertHours.Value = 6;
Dispatcher.UIThread.RunJobs();
if (scheduleLabels.Any(t => t.Parent is Border panel && !panel.IsVisible) || new ConfigurationStore(directory).Load().CardMaintenanceHours != 6) throw new Exception("One global threshold must apply to each matching account's notice.");
alertHours.Value = 168;
Dispatcher.UIThread.RunJobs();
configured.UpdateLayout();
var hoursEditor = alertHours.GetVisualDescendants().OfType<TextBox>().Single();
if (hoursEditor.Text != "168" || hoursEditor.Bounds.Width < 60) throw new Exception($"Maintenance hour editor clips three-digit values: {hoursEditor.Bounds.Width} pixels.");
if (new ConfigurationStore(directory).Load().CardMaintenanceHours != 168) throw new Exception("Three-digit alert timing did not persist.");
alertHours.Value = 12;
Dispatcher.UIThread.RunJobs();
Console.WriteLine("PASS: one global maintenance display threshold applies across cards and persists.");
Console.WriteLine("PASS: selected-only bulk policies, selection persistence, select-all, and missing/known SE identities.");
configured.FindControl<Expander>("LauncherSetup")!.IsExpanded = false;
Dispatcher.UIThread.RunJobs();
using (var frame = configured.CaptureRenderedFrame() ?? throw new Exception("No configured frame")) frame.Save("artifacts/dashboard-accounts.png", PngBitmapEncoderOptions.Default);
var nativePolOption = configured.FindControl<CheckBox>("NormalizePolWindowToggle")!;
if (nativePolOption.IsChecked != true) throw new Exception("Native POL sizing must be enabled by default.");
nativePolOption.IsChecked = false;
Dispatcher.UIThread.RunJobs();
if (new ConfigurationStore(directory).Load().NormalizePolWindow) throw new Exception("POL sizing opt-out did not persist.");
nativePolOption.IsChecked = true;
Dispatcher.UIThread.RunJobs();
if (!new ConfigurationStore(directory).Load().NormalizePolWindow) throw new Exception("POL sizing opt-in did not persist.");
// Exercise the actual running button without launching POL or reading credentials.
var cancellationFields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var loginStops = (Dictionary<Guid, CancellationTokenSource>)typeof(MainWindow).GetField("accountCancellation", cancellationFields)!.GetValue(configured)!;
var stoppedAccounts = (HashSet<Guid>)typeof(MainWindow).GetField("intentionallyStopped", cancellationFields)!.GetValue(configured)!;
var trackedSessions = (Dictionary<Guid, ManagedProcess>)typeof(MainWindow).GetField("sessions", cancellationFields)!.GetValue(configured)!;
var preservedSessions = trackedSessions.ToArray();
using (var batchStop = new CancellationTokenSource())
using (var activeStop = new CancellationTokenSource())
{
    var activeAccount = new ConfigurationStore(directory).Load().ConfiguredAccounts.First().Id;
    loginStops.Add(activeAccount, activeStop);
    typeof(MainWindow).GetField("autoLoginBatchStop", cancellationFields)!.SetValue(configured, batchStop);
    typeof(MainWindow).GetMethod("UpdateSelectionControls", cancellationFields)!.Invoke(configured, null);
    var stopButton = configured.FindControl<Button>("StartEnabledButton")!;
    if (!stopButton.IsEnabled || !stopButton.Classes.Contains("login-running") || !stopButton.Content!.ToString()!.Contains("Stop auto logins"))
        throw new Exception("Active automatic login must keep the stop button clickable and animated.");
    stopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    if (!batchStop.IsCancellationRequested || !activeStop.IsCancellationRequested || !stoppedAccounts.Contains(activeAccount))
        throw new Exception("Stop must cancel queued and active logins and suppress their recovery.");
    foreach (var enabledAccount in new ConfigurationStore(directory).Load().ConfiguredAccounts.Where(a => a.AutoLoginEnabled))
        if (!stoppedAccounts.Contains(enabledAccount.Id)) throw new Exception("An enabled queued account can resume after cancellation.");
    if (stopButton.IsEnabled || stopButton.Classes.Contains("login-running") || stopButton.Content!.ToString() != "Stopping auto logins…")
        throw new Exception("Stopping state must prevent a second start while cancellation drains.");
    if (!preservedSessions.SequenceEqual(trackedSessions.ToArray())) throw new Exception("Cancelling login changed existing tracked sessions.");
    loginStops.Clear();
    typeof(MainWindow).GetField("autoLoginBatchStop", cancellationFields)!.SetValue(configured, null);
    typeof(MainWindow).GetMethod("UpdateSelectionControls", cancellationFields)!.Invoke(configured, null);
    if (stopButton.Content!.ToString() != "Auto login enabled accounts" || stopButton.Classes.Contains("login-running"))
        throw new Exception("Idle auto login button did not recover after cancellation.");
}
stoppedAccounts.Clear();
Console.WriteLine("PASS: animated auto login button cancels batch and active input, blocks recovery, preserves sessions, and returns to idle.");
Console.WriteLine("PASS: configured dashboard and maintenance persistence.");
if (ThemeCatalog.All.Count != 16 || ThemeCatalog.All.Count(t => t.Artwork != null) != 12) throw new Exception("Theme picker must contain four dark UI and twelve artwork themes.");
if (ThemeCatalog.All.Any(t => t.Id == "maw") || ThemeCatalog.Resolve("maw") is not { Id: "abyssea", SideArtwork: "Maw.jpg", Artwork: "Illustration6.jpg" }) throw new Exception("Existing Cavernous Maw selections must resolve to the merged Abyssea theme.");
if (ThemeCatalog.Resolve("rakaznar") is not { Artwork: "Amano3.jpg", SideArtwork: "Promyvion.jpg" }) throw new Exception("Promyvion must retain its zone illustration on the left and use the chosen primary background.");
if (ThemeCatalog.Resolve("ffxi-original") is not { Artwork: "Illustration0.jpg", SideArtwork: "Anniversary2.jpg", SideFocus: .40 }) throw new Exception("Original FFXI must preserve the approved paired artwork and character crop.");
var themePicker = configured.FindControl<ComboBox>("ThemePicker")!;
foreach (var theme in ThemeCatalog.All)
{
    themePicker.SelectedItem = theme;
    Dispatcher.UIThread.RunJobs();
    if (theme.Artwork != null && (!configured.FindControl<Border>("PortraitFrame")!.IsVisible || configured.FindControl<Image>("PortraitArtwork")!.Source is not CroppedBitmap || configured.FindControl<Image>("BackgroundArtwork")!.Source is not CroppedBitmap)) throw new Exception("Every artwork theme needs aspect-preserving expanded crops.");
    if (theme.ContinuousArtwork)
    {
        var leftCrop = (CroppedBitmap)configured.FindControl<Image>("PortraitArtwork")!.Source!;
        var rightCrop = (CroppedBitmap)configured.FindControl<Image>("BackgroundArtwork")!.Source!;
        if (!ReferenceEquals(leftCrop.Source, rightCrop.Source) || leftCrop.SourceRect.Right != rightCrop.SourceRect.X || leftCrop.SourceRect.Y != rightCrop.SourceRect.Y || leftCrop.SourceRect.Height != rightCrop.SourceRect.Height) throw new Exception("Continuous artwork panels must share one image and adjoining source rectangles.");
        if (configured.FindControl<Image>("PortraitArtwork")!.Opacity != configured.FindControl<Image>("BackgroundArtwork")!.Opacity || !configured.FindControl<Border>("PortraitShade")!.IsVisible) throw new Exception("Continuous artwork must use matching brightness across the seam.");
    }
    if (theme.SideArtwork != null)
    {
        var sideCrop = (CroppedBitmap)configured.FindControl<Image>("PortraitArtwork")!.Source!;
        var backgroundCrop = (CroppedBitmap)configured.FindControl<Image>("BackgroundArtwork")!.Source!;
        if (ReferenceEquals(sideCrop.Source, backgroundCrop.Source) || (theme.Id == "amano-autumn" && backgroundCrop.SourceRect.Y != 0)) throw new Exception("Paired themes must use independent images; Autumn must preserve the top of its right artwork.");
    }
    if (new ConfigurationStore(directory).Load().Theme != theme.Id) throw new Exception("Selected theme did not persist.");
    if ((configured.FindControl<Image>("BackgroundArtwork")!.Source != null) != (theme.Artwork != null)) throw new Exception("Artwork theme did not load: " + theme.Id + " · " + configured.FindControl<TextBlock>("Activity")!.Text);
}
foreach (var id in new[] { "amano-map", "amano-zilart", "amano-blue" })
{
    themePicker.SelectedItem = ThemeCatalog.Resolve(id);
    foreach (var width in new[] { 900d, 1050d, 1440d })
    {
        configured.Width = width;
        Dispatcher.UIThread.RunJobs();
        var left = configured.FindControl<Image>("PortraitArtwork")!.Source as CroppedBitmap;
        var right = (CroppedBitmap)configured.FindControl<Image>("BackgroundArtwork")!.Source!;
        if (width < 1050 ? left != null : left == null || left.SourceRect.Right != right.SourceRect.X) throw new Exception("Continuous artwork must remain joined through expanded and narrow layouts.");
    }
}
configured.Width = 1120;
Dispatcher.UIThread.RunJobs();
themePicker.SelectedItem = ThemeCatalog.Resolve("abyssea");
Dispatcher.UIThread.RunJobs();
var savedArtwork = new ConfigurationStore(directory).Load().CharacterArtwork!;
if (savedArtwork.Count != 2 || savedArtwork.Values.Any(file => !ThemeCatalog.Landscapes.Contains(file))) throw new Exception("Every character needs a saved allowed landscape.");
configured.FindControl<Button>("ShuffleArtworkButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Dispatcher.UIThread.RunJobs();
var shuffledArtwork = new ConfigurationStore(directory).Load().CharacterArtwork!;
if (savedArtwork.Any(pair => shuffledArtwork[pair.Key] == pair.Value)) throw new Exception("Shuffle must choose a different landscape for every character.");
configured.FindControl<CheckBox>("LandscapeCardsToggle")!.IsChecked = false;
Dispatcher.UIThread.RunJobs();
if (new ConfigurationStore(directory).Load().LandscapeCards) throw new Exception("Card artwork preference must persist.");
var translucentCard = configured.FindControl<StackPanel>("AccountList")!.Children.OfType<Border>().First();
if (translucentCard.Background is not SolidColorBrush cardBrush || cardBrush.Color.A >= 160) throw new Exception("Disabling landscape cards must reveal the theme through translucent surfaces.");
foreach (var removed in new[] { "paper", "misareaux", "sorrows", "leafallia" })
    if (ThemeCatalog.Resolve(removed).Id != "midnight") throw new Exception("Removed themes must fall back to Midnight.");
configured.FindControl<CheckBox>("LandscapeCardsToggle")!.IsChecked = true;
Dispatcher.UIThread.RunJobs();
if (new ConfigurationStore(directory).Load().CharacterArtwork!.Any(pair => shuffledArtwork[pair.Key] != pair.Value)) throw new Exception("Toggling artwork must preserve chosen landscapes.");
configured.Width = 1300;
Dispatcher.UIThread.RunJobs();
if (!configured.FindControl<Border>("PortraitFrame")!.IsVisible) throw new Exception("Wide portrait theme needs a visible artwork strip.");
configured.Width = 900;
Dispatcher.UIThread.RunJobs();
if (configured.FindControl<Border>("PortraitFrame")!.IsVisible) throw new Exception("Narrow windows must preserve room for account controls.");
configured.Width = 1120;
Dispatcher.UIThread.RunJobs();
using (var frame = configured.CaptureRenderedFrame()!) frame.Save("artifacts/dashboard-ffxi-theme.png", PngBitmapEncoderOptions.Default);
themePicker.SelectedItem = ThemeCatalog.Resolve("nord");
Dispatcher.UIThread.RunJobs();
using (var frame = configured.CaptureRenderedFrame()!) frame.Save("artifacts/dashboard-nord-theme.png", PngBitmapEncoderOptions.Default);
themePicker.SelectedItem = ThemeCatalog.Resolve("midnight");
Dispatcher.UIThread.RunJobs();
if (ThemeCatalog.Resolve("unknown").Id != "midnight") throw new Exception("Unknown saved theme must fall back safely.");
configured.FindControl<Grid>("AddonCommandPanel")!.IsVisible = true;
configured.FindControl<Button>("CopyAddonCommandButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
Dispatcher.UIThread.RunJobs();
if (configured.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult() != "//lua load FFXIManager" || !configured.FindControl<TextBox>("InitialAddonCommand")!.IsReadOnly) throw new Exception("Copy action must write the displayed command and retain manual text selection.");
configured.FindControl<Grid>("AddonCommandPanel")!.IsVisible = false;
Console.WriteLine("PASS: all sixteen themes load, theme choice persists, unknown themes fall back, and command icon writes the displayed clipboard text.");
configured.Height = 2200;
Dispatcher.UIThread.RunJobs();
var beforeDrag = new ConfigurationStore(directory).Load().ConfiguredAccounts.ToArray();
Point HandleCenter(Guid id) => configured.GetLogicalDescendants().OfType<Border>().Single(b => b.Name == "Reorder_" + id.ToString("N")).TranslatePoint(new Point(50, 8), configured)!.Value;
var from = HandleCenter(mainAccount.Id);
var targetCard = configured.FindControl<StackPanel>("AccountList")!.Children.OfType<Border>().Single(b => Equals(b.Tag, secondAccount.Id));
var to = targetCard.TranslatePoint(new Point(50, targetCard.Bounds.Height - 8), configured)!.Value;
configured.MouseDown(from, MouseButton.Left);
configured.MouseMove(to, RawInputModifiers.LeftMouseButton);
if (configured.FindControl<Canvas>("DragLayer")!.Children.Count != 1) throw new Exception("Dragging must show a floating card preview.");
var ghost = configured.FindControl<Canvas>("DragLayer")!.Children.Single();
var ghostLeft = Canvas.GetLeft(ghost);
var ghostTop = Canvas.GetTop(ghost);
if (Math.Abs(ghost.Bounds.Width - targetCard.Bounds.Width) > 1 && Math.Abs(ghost.Width - targetCard.Bounds.Width) > 1) throw new Exception("Dragged card must keep its original width.");
configured.MouseMove(to + new Vector(10, -10), RawInputModifiers.LeftMouseButton);
if (Canvas.GetLeft(ghost) != ghostLeft || Canvas.GetTop(ghost) == ghostTop) throw new Exception("Reordering must slide vertically at the original horizontal position.");
if (!configured.FindControl<StackPanel>("AccountList")!.Children.OfType<Border>().Any(b => b.RenderTransform is TranslateTransform slide && slide.Y != 0)) throw new Exception("Other cards must preview the changing order.");
configured.MouseMove(to, RawInputModifiers.LeftMouseButton);
using (var frame = configured.CaptureRenderedFrame()!) frame.Save("artifacts/dashboard-drag-ghost.png", PngBitmapEncoderOptions.Default);
configured.MouseUp(to, MouseButton.Left);
Dispatcher.UIThread.RunJobs();
if (configured.FindControl<Canvas>("DragLayer")!.Children.Count != 0) throw new Exception("Drop must remove the floating preview.");
var reordered = new ConfigurationStore(directory).Load().ConfiguredAccounts;
if (!reordered.Select(a => a.Id).SequenceEqual(new[] { secondAccount.Id, mainAccount.Id }) || reordered.Any(a => a != beforeDrag.Single(b => b.Id == a.Id))) throw new Exception("Dragging must persist order without changing account configuration.");
if (configured.FindControl<TextBlock>("SelectionSummary")!.Text != "2 selected") throw new Exception("Dragging lost account selection.");
from = HandleCenter(mainAccount.Id);
configured.MouseDown(from, MouseButton.Left);
configured.MouseMove(new Point(-10, from.Y - 20), RawInputModifiers.LeftMouseButton);
configured.MouseUp(new Point(-10, from.Y - 20), MouseButton.Left);
Dispatcher.UIThread.RunJobs();
if (!new ConfigurationStore(directory).Load().ConfiguredAccounts.Select(a => a.Id).SequenceEqual(reordered.Select(a => a.Id))) throw new Exception("Dropping outside the list must not reorder accounts.");
Console.WriteLine("PASS: pointer drag persists character order, preserves account settings and selection, and rejects drops outside the list.");
configured.Close();
File.WriteAllText(Path.Combine(directory, "accounts.json"), "{broken");
var broken = new MainWindow(directory, false);
broken.Show();
Dispatcher.UIThread.RunJobs();
if (broken.FindControl<Button>("LaunchEnabledButton")!.IsEnabled) throw new Exception("Corrupt configuration must disable launch.");
broken.FindControl<Button>("LaunchEnabledButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
if (File.ReadAllText(Path.Combine(directory, "accounts.json")) != "{broken") throw new Exception("Corrupt configuration was overwritten.");
broken.Close();
Console.WriteLine("PASS: corrupt configuration is preserved.");
if (OperatingSystem.IsWindows())
{
    var vault = new WindowsCredentialVault(Path.Combine(directory, "vault"));
    var id = Guid.NewGuid();
    var secret = new AccountCredentials("test-POL-secret", "test-SE-secret", "test-member-secret");
    vault.Save(id, secret);
    if (vault.Read(id) != secret) throw new Exception("DPAPI round trip failed.");
    var file = Directory.GetFiles(Path.Combine(directory, "vault")).Single();
    if (System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file)).Contains("test-POL-secret")) throw new Exception("Plaintext credential found.");
    File.WriteAllBytes(file, [1, 2, 3]);
    try { vault.Read(id); throw new Exception("Corrupt vault accepted."); }
    catch (System.Security.Cryptography.CryptographicException) { }
    vault.Delete(id);
    if (vault.Contains(id)) throw new Exception("Credential deletion failed.");
    Console.WriteLine("PASS: Windows DPAPI encryption, corruption rejection, and deletion.");
}
else Console.WriteLine("SKIP: DPAPI tests require Windows.");
if (Directory.Exists(directory)) Directory.Delete(directory, true);

if (OperatingSystem.IsWindows())
{
    var discovered = new WindowsSessionPlatform().Discover();
    Console.WriteLine($"PASS: read-only Windows process discovery found {discovered.Count} POL session(s).");
    var uniqueGame = discovered.Where(p => p.Character != null).GroupBy(p => p.Character, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() == 1)?.Single();
    if (uniqueGame != null)
    {
        var lateDirectory = Path.Combine(Path.GetTempPath(), "ffxi-late-addon-" + Guid.NewGuid());
        var fakeRoot = Path.Combine(lateDirectory, "fixture-windower");
        var fakeData = Path.Combine(fakeRoot, "addons", "FFXIManager", "data");
        Directory.CreateDirectory(fakeData);
        var fakeWindower = Path.Combine(fakeRoot, "Windower.exe");
        File.WriteAllBytes(fakeWindower, []);
        var lateAccount = new AccountConfiguration(Guid.NewGuid(), "Late fixture", "", uniqueGame.Character!, "Odin", "", "");
        new ConfigurationStore(lateDirectory).Save(new LauncherConfiguration(WindowerPath: fakeWindower, PolPath: uniqueGame.ExecutablePath, Accounts: [lateAccount]));
        var lateWindow = new MainWindow(lateDirectory, false);
        try
        {
            lateWindow.Show();
            var adopt = typeof(MainWindow).GetMethod("AttachVerifiedSessions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var refresh = typeof(MainWindow).GetMethod("RefreshSessions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            bool HasPid() => lateWindow.GetLogicalDescendants().OfType<TextBlock>().Any(t => (t.Text ?? "").Contains("PID " + uniqueGame.Process.Pid));
            adopt.Invoke(lateWindow, null); refresh.Invoke(lateWindow, null);
            if (HasPid()) throw new Exception("Missing heartbeat cannot attach known-world process.");
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var wrong = Path.Combine(fakeData, "asura_" + uniqueGame.Character!.ToLowerInvariant() + ".status");
            File.WriteAllLines(wrong, ["1234567890abcdef_123456", uniqueGame.Character!, "Asura", now.ToString(), now.ToString()]);
            adopt.Invoke(lateWindow, null); refresh.Invoke(lateWindow, null);
            if (HasPid()) throw new Exception("Wrong-world late heartbeat cannot attach process.");
            File.Delete(wrong);
            var correct = Path.Combine(fakeData, "odin_" + uniqueGame.Character!.ToLowerInvariant() + ".status");
            File.WriteAllLines(correct, ["1234567890abcdef_123456", uniqueGame.Character!, "Odin", now.ToString(), now.ToString()]);
            adopt.Invoke(lateWindow, null); refresh.Invoke(lateWindow, null);
            if (!HasPid()) throw new Exception("Fresh heartbeat appearing after startup must attach existing configured account.");
            var timingLabel = lateWindow.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "SessionTiming_" + lateAccount.Id.ToString("N"));
            if (!timingLabel.IsVisible || !timingLabel.Text!.Contains("POL started:") || !timingLabel.Text.Contains("Session elapsed:") || !timingLabel.Text.Contains("Last recovered: —"))
                throw new Exception("Adopted POL must show actual process start and elapsed without invented recovery history.");
            var recordRecovery = typeof(MainWindow).GetMethod("RecordRecoveredSession", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            recordRecovery.Invoke(lateWindow, [lateAccount.Id, uniqueGame.Process with { StartedUtc = uniqueGame.Process.StartedUtc.AddSeconds(1) }]);
            refresh.Invoke(lateWindow, null);
            if (!timingLabel.Text!.Contains("Last recovered: —")) throw new Exception("Replacement process identity cannot reset the tracked session timer.");
            recordRecovery.Invoke(lateWindow, [lateAccount.Id, uniqueGame.Process]);
            refresh.Invoke(lateWindow, null);
            if (timingLabel.Text!.Contains("Last recovered: —") || !timingLabel.Text.Contains("Session elapsed: 00:00:"))
                throw new Exception("Successful recovery resets only the matching account's elapsed timer.");
            var savedTiming = new ManagedSessionStore(lateDirectory).Load().Single();
            if (savedTiming.LastRecoveredUtc == null || savedTiming.Process != uniqueGame.Process)
                throw new Exception("Successful recovery must save its timestamp with the exact managed process.");
            var reopened = new MainWindow(lateDirectory, false);
            try
            {
                typeof(MainWindow).GetMethod("RestoreManagedSessions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(reopened, null);
                refresh.Invoke(reopened, null);
                var restoredTiming = reopened.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "SessionTiming_" + lateAccount.Id.ToString("N"));
                if (!restoredTiming.IsVisible || restoredTiming.Text!.Contains("Last recovered: —") || !restoredTiming.Text.Contains(savedTiming.LastRecoveredUtc.Value.ToLocalTime().ToString("MMM d, yyyy h:mm:ss tt")))
                    throw new Exception("Manager restart must restore the recorded successful recovery timestamp.");
            }
            finally { reopened.Close(); }
            Console.WriteLine("PASS: per-account POL start/elapsed/recovery display resets only for exact process identity using isolated manager state; no game input.");
            var state = Path.Combine(fakeData, "odin_" + uniqueGame.Character!.ToLowerInvariant() + ".state");
            var connection = Path.Combine(fakeData, "odin_" + uniqueGame.Character!.ToLowerInvariant() + ".connection");
            var header = new[] { "1234567890abcdef_123456", uniqueGame.Character!, "Odin", now.ToString(), now.ToString() };
            File.WriteAllLines(state, header.Concat(new[] { "100", "100", "50", "50", "0", "100", "Fixture zone", "0" }));
            File.WriteAllLines(connection, header.Append(now.ToString()));
            refresh.Invoke(lateWindow, null);
            string LiveStatus() => lateWindow.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "SessionStatus_" + lateAccount.Id.ToString("N")).Text!;
            if (!LiveStatus().Contains("Connected") || LiveStatus().Contains("unverified")) throw new Exception("Matching fresh character and server reports must show Connected, including when Maintain connection is off.");
            var hints = (Dictionary<Guid, string>)typeof(MainWindow).GetField("connectionStates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(lateWindow)!;
            hints[lateAccount.Id] = "Disconnected (FFXI-4001) · reconnecting…";
            refresh.Invoke(lateWindow, null);
            if (!LiveStatus().Contains("Disconnected")) throw new Exception("A confirmed disconnect must take precedence over a still-rendering heartbeat.");
            hints.Clear();
            File.Delete(correct); refresh.Invoke(lateWindow, null);
            if (LiveStatus().Contains("Connected")) throw new Exception("An absent fresh heartbeat cannot retain Connected from the report cache.");
            File.WriteAllLines(correct, header.Select((v, i) => i == 3 ? new DateTimeOffset(uniqueGame.Process.StartedUtc).AddMinutes(-1).ToUnixTimeSeconds().ToString() : v));
            refresh.Invoke(lateWindow, null);
            if (LiveStatus().Contains("Connected")) throw new Exception("Old-process reports cannot verify a replacement process.");
            Console.WriteLine("PASS: real dashboard status uses fresh process/session-bound reports, retains disconnect priority, and rejects missing/old-process evidence without game input.");
            Console.WriteLine("PASS: late addon attachment refreshes known-world accounts; missing/wrong-world heartbeats rejected. Only isolated telemetry and read-only process inspection used.");
        }
        finally { lateWindow.Close(); Directory.Delete(lateDirectory, true); }
    }
    else Console.WriteLine("SKIP: late-addon native attachment check requires a running character process.");
    var importedDirectory = Path.Combine(Path.GetTempPath(), "ffxi-import-" + Guid.NewGuid());
    var importedWindow = new MainWindow(importedDirectory);
    try
    {
        importedWindow.Show();
        Dispatcher.UIThread.RunJobs();
        var installation = WindowsInstallationDiscovery.Detect();
        var detectedConfiguration = new ConfigurationStore(importedDirectory).Load();
        if (installation.WindowerPath != null && detectedConfiguration.WindowerPath != installation.WindowerPath || installation.PolPath != null && detectedConfiguration.PolPath != installation.PolPath) throw new Exception("Detected Windows paths must be filled and persisted at startup.");
        Console.WriteLine($"PASS: installation detection; Windower={installation.WindowerPath != null}, POL={installation.PolPath != null}, profiles={installation.Profiles.Count}.");
        var imported = detectedConfiguration.ConfiguredAccounts;
        var expected = discovered.Where(p => p.Character != null).GroupBy(p => p.Character, StringComparer.OrdinalIgnoreCase).Count(g => g.Count() == 1);
        if (imported.Count != expected || imported.Any(a => a.AutoLoginEnabled || a.ScheduleMaintenance)) throw new Exception("Startup import must create disabled drafts for unique character sessions.");
        Console.WriteLine($"PASS: startup imported {imported.Count} running character(s) into isolated account sections without game input.");
    }
    finally { importedWindow.Close(); if (Directory.Exists(importedDirectory)) Directory.Delete(importedDirectory, true); }
}

if (OperatingSystem.IsWindows())
{
    var installRoot = Path.Combine(Path.GetTempPath(), "ffxi-install-" + Guid.NewGuid());
    try
    {
        Directory.CreateDirectory(Path.Combine(installRoot, "scripts"));
        var fakeExe = Path.Combine(installRoot, "Windower.exe");
        File.WriteAllBytes(fakeExe, []); // Fixture is never executed.
        if (WindowsAddonDeployment.RunHelper([WindowsAddonDeployment.HelperArgument, fakeExe, "S-1-5-18"]) != 3 || Directory.Exists(Path.Combine(installRoot, "addons"))) throw new Exception("Helper must reject another user's SID before mutating installation.");
        if (WindowsAddonDeployment.RunHelper([WindowsAddonDeployment.HelperArgument]) != 2) throw new Exception("Helper must reject malformed invocation.");
        var init = Path.Combine(installRoot, "scripts", "init.txt");
        var original = System.Text.Encoding.UTF8.GetBytes("wait 5\r\nlua load OtherAddon\r\n");
        File.WriteAllBytes(init, original);
        var payload = Path.Combine(AppContext.BaseDirectory, "windower_addons", "FFXIManager");
        var permissionRequested = false;
        var deployed = WindowsAddonDeployment.InstallAsync(fakeExe, () => permissionRequested = true).GetAwaiter().GetResult();
        if (permissionRequested) throw new Exception("Writable installation must not request elevation.");
        if (!deployed.StartupAdded || deployed.BackupPath == null || !File.ReadAllBytes(deployed.BackupPath).SequenceEqual(original) || !File.ReadAllBytes(init).AsSpan().EndsWith(original)) throw new Exception("Deployment must preserve and back up existing init bytes.");
        var tokenPath = Path.Combine(deployed.AddonDirectory, "data", "control.token");
        var token = File.ReadAllText(tokenPath);
        var alertPath = Path.Combine(deployed.AddonDirectory, "data", "preserved.alert");
        File.WriteAllText(alertPath, "fixture telemetry");
        var secondDeployment = WindowerAddonInstaller.Install(fakeExe, payload);
        if (secondDeployment.StartupAdded || secondDeployment.BackupPath != null || File.ReadAllText(tokenPath) != token || File.ReadAllText(alertPath) != "fixture telemetry") throw new Exception("Redeploy must preserve startup, token and live telemetry.");
        var deployedLua = Path.Combine(deployed.AddonDirectory, "FFXIManager.lua");
        File.Delete(deployedLua);
        using (var existingManagerMutex = new Mutex(false, "FFXILauncher-" + Environment.UserName))
        using (var user = System.Security.Principal.WindowsIdentity.GetCurrent())
        {
            var helperStart = new System.Diagnostics.ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "FFXILauncher.Desktop.exe")) { UseShellExecute = false, CreateNoWindow = true };
            helperStart.ArgumentList.Add(WindowsAddonDeployment.HelperArgument);
            helperStart.ArgumentList.Add(fakeExe);
            helperStart.ArgumentList.Add(user.User!.Value);
            using var helper = System.Diagnostics.Process.Start(helperStart) ?? throw new Exception("Helper did not start.");
            if (!helper.WaitForExit(15000)) { helper.Kill(); throw new Exception("Helper did not exit; it may have opened the desktop UI."); }
            if (helper.ExitCode != 0 || !File.Exists(deployedLua) || File.ReadAllText(tokenPath) != token) throw new Exception("Helper entry point must deploy while manager mutex exists, preserve token and return success.");
        }
        Console.WriteLine("PASS: helper entry point bypasses manager mutex and deploys without opening another UI.");
        File.Delete(init);
        if (!WindowerAddonInstaller.Install(fakeExe, payload).StartupAdded || !File.ReadAllText(init).Contains("lua load FFXIManager")) throw new Exception("New installation must create startup script.");
        File.WriteAllText(tokenPath, "invalid");
        try { WindowerAddonInstaller.Install(fakeExe, payload); throw new Exception("Invalid existing token was accepted."); }
        catch (InvalidDataException) { }
        if (File.ReadAllText(tokenPath) != "invalid") throw new Exception("Invalid token must not be silently replaced.");
        Console.WriteLine("PASS: isolated Windows addon deployment, exact init backup, idempotence, token/telemetry preservation, new init and invalid-token rejection.");
    }
    finally { if (Directory.Exists(installRoot)) Directory.Delete(installRoot, true); }
}

if (OperatingSystem.IsWindows())
{
    var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
    System.Diagnostics.Process StartFixture()
    {
        var start = new System.Diagnostics.ProcessStartInfo(powershell) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var value in new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 60" }) start.ArgumentList.Add(value);
        return System.Diagnostics.Process.Start(start) ?? throw new Exception("Could not start disposable process fixture.");
    }
    using var target = StartFixture();
    using var other = StartFixture();
    try
    {
        var owned = new ManagedProcess(target.Id, target.StartTime.ToUniversalTime());
        try { WindowsProcessCleanup.TerminateAsync(owned with { StartedUtc = owned.StartedUtc.AddSeconds(1) }, powershell, CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("Replacement process timestamp accepted."); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("replacement")) { }
        if (target.HasExited) throw new Exception("Wrong start time killed process.");
        try { WindowsProcessCleanup.TerminateAsync(owned, Path.Combine(Path.GetTempPath(), "wrong.exe"), CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("Wrong executable accepted."); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("executable")) { }
        if (target.HasExited) throw new Exception("Wrong executable killed process.");
        WindowsProcessCleanup.TerminateAsync(owned, powershell, CancellationToken.None).GetAwaiter().GetResult();
        if (!target.HasExited || other.HasExited) throw new Exception("Cleanup must terminate only the exact owned fixture.");
        WindowsProcessCleanup.TerminateAsync(owned, powershell, CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine("PASS: exact process termination and exit verification; wrong creation time/executable rejected; unrelated disposable process preserved.");
    }
    finally { if (!target.HasExited) { target.Kill(); target.WaitForExit(); } if (!other.HasExited) { other.Kill(); other.WaitForExit(); } }
}
