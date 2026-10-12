using System.Diagnostics;
using System.Text.Json;
using FFXILauncher.Core;

namespace FFXILauncher.Desktop.Platform;

internal sealed class WindowsLoginEngine(string directory, LauncherConfiguration config, AccountConfiguration account, ManagedProcess process, Action<string> report)
{
    private readonly WindowsLoginWindow window = new(process, config.PolPath);
    private readonly string framePath = Path.Combine(directory, "frame.bmp");
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private bool identityVerified, passwordEntered, editorOpened, selectedCharacterVerified, characterConfirmed, returnedForIdentity, worldVerified;
    public bool FullFlowValidated => identityVerified && passwordEntered && selectedCharacterVerified && characterConfirmed && worldVerified;
    internal string? MemberSectionResult { get; private set; }
    private int characterConfirmAttempts;
    private readonly Dictionary<LoginCheckpoint, int> visits = [];
    private readonly Dictionary<LoginCheckpoint, int> enterRetries = [];
    private Process? ocr;
    private string? selectedMemberLabel;
    private string[] selectedMemberLabels = [];
    private bool SelectedMemberMatches(LoginFrame frame) => selectedMemberLabels.Any(label => LoginCheckpoints.SelectedMemberLabelMatches(frame, label));
    private bool returnedToMemberList;
    private int memberSelectionRetries;
    private bool memberSelectedByExactId;
    private async Task<LoginFrame> ReadFrame(CancellationToken cancellation, bool loadingOnly = false, bool confirmationOnly = false, bool characterGridOnly = false)
    {
        var clock = Stopwatch.StartNew();
        var gameFrame = false;
        var captureUntil = DateTimeOffset.UtcNow.AddSeconds(2);
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var dpi = WindowsLoginWindow.SetThreadDpiAwarenessContext(new(-4));
            var captured = false;
            try { window.Capture(framePath); gameFrame = window.IsGameWindow; captured = true; }
            catch (LoginObscuredException) when (DateTimeOffset.UtcNow < captureUntil) { }
            finally { if (dpi != IntPtr.Zero) WindowsLoginWindow.SetThreadDpiAwarenessContext(dpi); }
            if (captured) break;
            // A transient overlay is a wait state. Never send keys through it
            // or reclaim focus from another application.
            await Task.Delay(100, cancellation);
        }
        var captureMs = clock.ElapsedMilliseconds;
        if (ocr == null)
        {
            var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new System.Text.UTF8Encoding(false), StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", AppPayload.FilePath("tools/windows-ocr.ps1"), "-ImagePath", framePath, "-Server" }) info.ArgumentList.Add(arg);
            ocr = Process.Start(info) ?? throw new LoginSafetyException("Windows OCR could not start.");
            // Drain diagnostics without logging potentially sensitive screen data.
            _ = ocr.StandardError.ReadToEndAsync();
        }
        if (ocr.HasExited) throw new LoginSafetyException("Windows OCR stopped. Login input was paused.");
        var readCommand = characterGridOnly ? "read-grid" : confirmationOnly ? "read-confirmation" : gameFrame ? "read-game" : "read-pol";
        if (loadingOnly && !characterGridOnly && !confirmationOnly) readCommand += "-loading";
        await ocr.StandardInput.WriteLineAsync(readCommand.AsMemory(), cancellation);
        await ocr.StandardInput.FlushAsync(cancellation);
        var text = await ocr.StandardOutput.ReadLineAsync(cancellation).AsTask().WaitAsync(TimeSpan.FromSeconds(15), cancellation);
        if (text == null || text.Length > 200000) throw new LoginSafetyException("Windows OCR returned no usable screen evidence.");
        var frame = JsonSerializer.Deserialize<LoginFrame>(text, Json) ?? throw new LoginSafetyException("Windows OCR returned no screen evidence.");
        // Keep bounded, credential-free timing evidence for live optimization.
        // Never persist recognized text, account IDs, words or password contents.
        var timingPath = Path.Combine(directory, "screen-timings.jsonl");
        try
        {
            if (!File.Exists(timingPath) || new FileInfo(timingPath).Length < 256000)
                File.AppendAllText(timingPath, JsonSerializer.Serialize(new { CapturedUtc = DateTimeOffset.UtcNow, CaptureMs = captureMs,
                    ReadMs = clock.ElapsedMilliseconds - captureMs, LoadingOnly = loadingOnly, ConfirmationOnly = confirmationOnly, Checkpoint = LoginCheckpoints.Recognize(frame).ToString() }) + "\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return frame;
    }

    private void LogPolPathEvent(string action, LoginCheckpoint checkpoint)
    {
        try
        {
            var path = Path.Combine(directory, "pol-path-events.jsonl");
            if (!File.Exists(path) || new FileInfo(path).Length < 64000)
                File.AppendAllText(path, JsonSerializer.Serialize(new { Utc = DateTimeOffset.UtcNow, Action = action, Checkpoint = checkpoint.ToString() }) + "\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void Click(LoginFrame frame, string text, int pressMilliseconds = 80)
    {
        var line = (text is "Yes" or "No" && LoginCheckpoints.Recognize(frame) == LoginCheckpoint.CharacterConfirmation
            ? LoginCheckpoints.ConfirmationButton(frame, text) : text is "Cancel" or "Connect" && LoginCheckpoints.Recognize(frame) == LoginCheckpoint.Connect
                ? LoginCheckpoints.PolDialogButton(frame, text) : text is "Log In" or "Back" && LoginCheckpoints.Recognize(frame) == LoginCheckpoint.MemberInformation
                    ? LoginCheckpoints.PolMenuButton(frame, text) : frame.UniqueLine(text))
            ?? throw new LoginSafetyException(text is "Connect" or "Cancel" or "Back" or "Log In" or "Yes" or "No"
                ? $"The {text} button could not be identified uniquely. Input stopped."
                : "A required login control could not be identified uniquely.");
        if (line.Words.Length == 0) throw new LoginSafetyException("Login control coordinates are unavailable.");
        var x = (line.Words.Min(w => w.X) + line.Words.Max(w => w.X + w.Width)) / 2;
        var y = (line.Words.Min(w => w.Y) + line.Words.Max(w => w.Y + w.Height)) / 2;
        window.Click(x, y, frame.Width, frame.Height, pressMilliseconds);
    }
    public async Task RunAsync(ICredentialVault vault, bool inspectOnly, CancellationToken cancellation, bool memberSectionOnly = false, int pressMs = 80, int pollMs = 50, string trainingSection = "", int menuGapMs = 0)
    {
        try { await RunCoreAsync(vault, inspectOnly, cancellation, memberSectionOnly, pressMs, pollMs, trainingSection, menuGapMs); }
        finally
        {
            if (ocr != null)
            {
                try { if (!ocr.HasExited) { ocr.Kill(); await ocr.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); } }
                finally { ocr.Dispose(); ocr = null; }
            }
        }
    }
    private async Task ReturnToGameMenu(CancellationToken cancellation)
    {
        var clock = Stopwatch.StartNew();
        var ipc = new ShutdownIpc(Path.Combine(Path.GetDirectoryName(config.WindowerPath)!, "addons", "FFXIManager", "data"));
        var first = ipc.ReadSession(account, DateTimeOffset.UtcNow) ?? throw new LoginSafetyException("A fresh configured-character heartbeat is required to return to the game menu.");
        if (first.RegisteredUtc < process.StartedUtc.AddSeconds(-2)) throw new LoginSafetyException("Character heartbeat predates the owned process. No command sent.");
        GameSession? current = null;
        while (clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            cancellation.ThrowIfCancellationRequested();
            current = ipc.ReadSession(account, DateTimeOffset.UtcNow);
            if (current != null && ShutdownIpc.HasAdvanced(first, current)) break;
            if (current == null || current.Nonce != first.Nonce) throw new LoginSafetyException("The character session changed before menu return. No command sent.");
            await Task.Delay(100, cancellation);
        }
        if (current == null || !ShutdownIpc.HasAdvanced(first, current)) throw new LoginSafetyException("Character heartbeat did not advance. No command sent.");
        using (var owned = Process.GetProcessById(process.Pid))
            if (owned.StartTime.ToUniversalTime() != process.StartedUtc || !owned.MainWindowTitle.Trim().Equals(account.Character, StringComparison.OrdinalIgnoreCase))
                throw new LoginSafetyException("Owned character window changed. No command sent.");
        var dpi = WindowsLoginWindow.SetThreadDpiAwarenessContext(new(-4));
        try { window.ReturnToGameMenu(account.Character, cancellation); }
        finally { if (dpi != IntPtr.Zero) WindowsLoginWindow.SetThreadDpiAwarenessContext(dpi); }
        report("Sent one logout command to the verified character; keeping POL authenticated for menu testing…");
        while (clock.Elapsed < TimeSpan.FromSeconds(60))
        {
            cancellation.ThrowIfCancellationRequested();
            var session = ipc.ReadSession(account, DateTimeOffset.UtcNow);
            if (session != null)
            {
                if (session.Nonce != first.Nonce) throw new LoginSafetyException("Another character session appeared during menu return. Input stopped.");
                await Task.Delay(100, cancellation); continue;
            }
            var frame = await ReadFrame(cancellation, loadingOnly: true);
            var stage = LoginCheckpoints.Recognize(frame);
            if (stage is LoginCheckpoint.Title or LoginCheckpoint.Characters || LoginCheckpoints.CanOpenCharacterList(frame) || LoginCheckpoints.CanOpenDefaultCharacterConfirmation(frame))
            {
                MemberSectionResult = $"Authenticated FFXI menu reached in {clock.ElapsedMilliseconds} ms. One logout command; no credentials, process termination, or further Enter sent.";
                return;
            }
            if (stage is not LoginCheckpoint.Unknown) throw new LoginSafetyException("Menu return left the expected game route. No credentials or further input sent.");
            await Task.Delay(50, cancellation);
        }
        throw new LoginSafetyException("Menu return did not complete. No logout retry, process kill, or credential submission was sent.");
    }
    private async Task RunCharacterSuccessTrial(LoginControlCache confirmations, int pressMs, int pollMs, int gapMs, CancellationToken cancellation)
    {
        if (pressMs is < 1 or > 80 || pollMs is < 1 or > 50 || gapMs is < 0 or > 3000 || !window.IsGameWindow)
            throw new LoginSafetyException("Success trial requires the owned character menu and bounded timing.");
        // This explicit QA operation tests the already learned default path.
        // The cache is bound to account, exact character/world and profile.
        // Never rediscover the handwritten row or read between Enter presses.
        if (!confirmations.HasReplay) throw new LoginSafetyException("Learn the exact configured default-character confirmation before success trials.");
        var frame = await ReadFrame(cancellation, loadingOnly: true);
        if (!LoginCheckpoints.CanOpenDefaultCharacterConfirmation(frame))
            throw new LoginSafetyException("Success trial requires the existing character-selection screen. No route input sent.");
        var dpi = WindowsLoginWindow.SetThreadDpiAwarenessContext(new(-4));
        var clock = Stopwatch.StartNew();
        try
        {
            cancellation.ThrowIfCancellationRequested();
            window.ConfirmGameSelection(pressMs);
            if (cancellation.WaitHandle.WaitOne(gapMs)) cancellation.ThrowIfCancellationRequested();
            cancellation.ThrowIfCancellationRequested();
            window.ConfirmGameSelection(pressMs);
        }
        finally { if (dpi != IntPtr.Zero) WindowsLoginWindow.SetThreadDpiAwarenessContext(dpi); }
        var inputMs = clock.ElapsedMilliseconds;
        report($"Successful-route Enter pair sent in {inputMs} ms with {gapMs} ms gap; no intermediate screen read.");
        var ipc = new ShutdownIpc(Path.Combine(Path.GetDirectoryName(config.WindowerPath)!, "addons", "FFXIManager", "data"));
        GameSession? observed = null;
        while (clock.Elapsed < TimeSpan.FromSeconds(30))
        {
            cancellation.ThrowIfCancellationRequested();
            var session = ipc.DiscoverCharacter(account.Character, new DateTimeOffset(process.StartedUtc), DateTimeOffset.UtcNow);
            if (session != null && session.RegisteredUtc >= process.StartedUtc.AddSeconds(-2))
            {
                using var owned = Process.GetProcessById(process.Pid);
                if (owned.StartTime.ToUniversalTime() != process.StartedUtc) throw new LoginSafetyException("Trial process identity changed.");
                if (!session.World.Equals(account.World, StringComparison.OrdinalIgnoreCase)) throw new LoginSafetyException("Trial world does not match configuration.");
                if (owned.MainWindowTitle.Trim().Equals(account.Character, StringComparison.OrdinalIgnoreCase))
                {
                    if (observed != null && ShutdownIpc.HasAdvanced(observed, session))
                    {
                        MemberSectionResult = $"Successful character route verified in {clock.ElapsedMilliseconds} ms; input {inputMs} ms, gap {gapMs} ms. No credentials or intermediate OCR.";
                        return;
                    }
                    observed = session;
                }
            }
            await Task.Delay(pollMs, cancellation);
        }
        throw new LoginSafetyException("Early Enter pair did not produce verified world entry. No extra Enter, logout, or credentials sent.");
    }

    private async Task RunCoreAsync(ICredentialVault vault, bool inspectOnly, CancellationToken cancellation, bool memberSectionOnly, int pressMs, int pollMs, string trainingSection, int menuGapMs)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!Path.GetFileName(config.PolPath).Equals("pol.exe", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(config.WindowerPath).Equals("Windower.exe", StringComparison.OrdinalIgnoreCase))
            throw new LoginSafetyException("Configure the installed Windower.exe and pol.exe before calibration.");
        report("Waiting for the owned POL window to open…");
        await window.WaitForWindowAsync(cancellation);
        var dpi = WindowsLoginWindow.SetThreadDpiAwarenessContext(new(-4));
        try
        {
            window.Focus();
            if (!inspectOnly && config.NormalizePolWindow) window.NormalizePolClient();
            if (!inspectOnly && window.IsGameWindow) window.ParkCursor();
        }
        finally { if (dpi != IntPtr.Zero) WindowsLoginWindow.SetThreadDpiAwarenessContext(dpi); }
        if (inspectOnly) { await ReadFrame(cancellation); report("Calibration capture saved."); return; }
        if (trainingSection == "return")
        {
            await ReturnToGameMenu(cancellation);
            return;
        }
        if (LoginCheckpoints.Requirements(account) is {} requirement) throw new LoginSafetyException(requirement);
        var training = new LoginTrainingLedger(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FFXILauncher", "login-training"));
        var accountBinding = LoginTrainingLedger.IdentityBinding(account.SquareEnixId);
        LoginFrame? resumedMenu = null;
        if (trainingSection is "play" or "continue" or "character")
        {
            if (training.MatchesAuthenticated(account.Id, process, accountBinding)) { identityVerified = true; passwordEntered = true; }
            else
            {
                // A stopped older checkpoint may already be in FFXI. Resume
                // only reversible game menus; never infer POL account proof.
                var resumed = await ReadFrame(cancellation);
                if (trainingSection == "play" || !window.IsGameWindow ||
                    (LoginCheckpoints.Recognize(resumed) is not (LoginCheckpoint.Agreement or LoginCheckpoint.Title or LoginCheckpoint.Characters or LoginCheckpoint.CharacterConfirmation) &&
                     !LoginCheckpoints.CanOpenDefaultCharacterConfirmation(resumed) && !LoginCheckpoints.CanOpenCharacterList(resumed) &&
                     !(trainingSection == "character" && LoginCheckpoints.ConfirmationCancelPoint(resumed, account.Character) != null)))
                    throw new LoginSafetyException("No matching authenticated training proof or reversible FFXI menu. Credentials will not be entered.");
                resumedMenu = resumed;
            }
        }
        var sectionClock = Stopwatch.StartNew();
        void RecordAction(LoginCheckpoint checkpoint)
        {
            // Record completed input calls, never recognized text, IDs,
            // coordinates or credentials. Screen reads alone cannot separate
            // observation latency from a completed action's transition time.
            var path = Path.Combine(directory, "action-timings.jsonl");
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length < 256000)
                    File.AppendAllText(path, JsonSerializer.Serialize(new { ActionUtc = DateTimeOffset.UtcNow,
                        ElapsedMs = sectionClock.ElapsedMilliseconds, Checkpoint = checkpoint.ToString() }) + "\n");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        long promptMs = 0, passwordMs = 0;
        var submitted = false;
        var playOpened = false;
        var characterCancelled = false;
        DateTimeOffset? characterCancelAt = null;
        DateTimeOffset? playBackAt = null;
        var ipc = new ShutdownIpc(Path.Combine(Path.GetDirectoryName(config.WindowerPath)!, "addons", "FFXIManager", "data"));
        var deadline = DateTimeOffset.UtcNow.AddMinutes(6);
        Stopwatch? memberSectionClock = null;
        DateTimeOffset? cachedLogInAt = null;
        DateTimeOffset stageSince = DateTimeOffset.UtcNow;
        LoginCheckpoint previous = LoginCheckpoint.Unknown; var unknownSince = DateTimeOffset.UtcNow;
        var settingsPath = Path.Combine(Path.GetDirectoryName(config.WindowerPath)!, "settings.xml");
        string profileSettings;
        try
        {
            var profile = WindowsInstallationDiscovery.ResolveProfile(config.WindowerPath, account.Profile);
            profileSettings = profile == null ? Guid.NewGuid().ToString("N") : LoginTimingCache.ProfileSettingsBinding(settingsPath, profile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        { profileSettings = Guid.NewGuid().ToString("N"); } // Unreadable settings cannot reuse old hints.
        var cacheBinding = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Version = 2, account.Id, account.Profile, account.Character, account.World,
            account.SquareEnixId, config.WindowerPath, config.PolPath, account.UsesOneTimePassword,
            account.PolPasswordStoredInPol, account.MemberPasswordRequired, profileSettings }))));
        var timing = new LoginTimingCache(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FFXILauncher", "login-timing"), cacheBinding);
        var controls = new LoginControlCache(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FFXILauncher", "login-controls"), JsonSerializer.Serialize(new { Version = 1, config.PolPath,
                PolVersion = File.GetLastWriteTimeUtc(config.PolPath).Ticks }));
        var confirmations = new LoginControlCache(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FFXILauncher", "character-confirmations"), JsonSerializer.Serialize(new { Version = 2, cacheBinding }));
        if (trainingSection == "success")
        {
            await RunCharacterSuccessTrial(confirmations, pressMs, pollMs, menuGapMs, cancellation);
            return;
        }
        if (!memberSectionOnly && trainingSection != "character") { pressMs = controls.ReplayPressMs; pollMs = controls.ReplayPollMs; }
        var gamePressMs = trainingSection == "character" ? pressMs : Math.Min(40, confirmations.ReplayPressMs);
        var characterPollMs = trainingSection == "character" ? pollMs : confirmations.ReplayPollMs;
        var characterTrialPassed = false;
        var characterOpenedThisTrial = false;
        var polEnterPath = new VerifiedPolEnterPath();
        var confirmationPolls = 0;
        var loadingPollUntil = DateTimeOffset.UtcNow;
        var lastCharacterSelection = DateTimeOffset.MinValue;
        DateTimeOffset? openingGameUntil = null;
        var gameHandoffFocused = false;
        var startupCaptureUntil = DateTimeOffset.UtcNow.AddSeconds(15);
        var loadingReads = 0;
        LoginTimingCache.CharacterPath? replayPath = null;
        var replayUsed = false;
        var confirmationRecoveryUsed = false;
        var confirmationSentAt = DateTimeOffset.MinValue;
        var titleEnteredAt = DateTimeOffset.MinValue;
        GameSession? worldObservation = null;
        bool CanReplaySuccessfulDefault() => trainingSection == "" && identityVerified && passwordEntered &&
            titleEnteredAt != DateTimeOffset.MinValue && replayPath != null && confirmations.HasReplay;
        void ReplaySuccessfulDefault()
        {
            // This account/profile path already has two full successful logins
            // proving the default selection. QA accepted 40 ms holds with no
            // added gap; 20/30 ms missed the second confirm. Do not read the
            // animating dialog between these presses. Verify the actual world
            // character afterwards, and revoke replay if completion fails.
            replayUsed = true;
            cancellation.ThrowIfCancellationRequested();
            window.ConfirmGameSelection(40);
            RecordAction(LoginCheckpoint.Characters);
            cancellation.ThrowIfCancellationRequested();
            window.ConfirmGameSelection(40);
            selectedCharacterVerified = true;
            characterConfirmed = true; confirmationSentAt = DateTimeOffset.UtcNow;
            RecordAction(LoginCheckpoint.CharacterConfirmation);
            report("Replayed validated default-character Enter pair with no intermediate OCR; verifying character/world heartbeat…");
        }

        try
        {
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellation.ThrowIfCancellationRequested();
                var session = memberSectionOnly || trainingSection is "password" or "play" ? null : ipc.DiscoverCharacter(account.Character, new DateTimeOffset(process.StartedUtc), DateTimeOffset.UtcNow);
                if (session != null && session.RegisteredUtc >= new DateTimeOffset(process.StartedUtc).AddSeconds(-2))
                {
                    using var p = Process.GetProcessById(process.Pid);
                    if (p.StartTime.ToUniversalTime() == process.StartedUtc && p.MainWindowTitle.Trim().Equals(account.Character, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!session.World.Equals(account.World, StringComparison.OrdinalIgnoreCase)) throw new LoginSafetyException($"The addon reports {account.Character} on {session.World}; configured world is {account.World}. Correct the account mapping before retrying.");
                        if (worldObservation != null && ShutdownIpc.HasAdvanced(worldObservation, session))
                        {
                            worldVerified = true;
                            selectedCharacterVerified = true; characterConfirmed = true;
                            if (FullFlowValidated)
                            {
                                try { timing.CommitSuccessfulRun(); controls.CommitSuccessfulRun(); confirmations.CommitSuccessfulRun(); }
                                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                            }
                            return;
                        }
                        worldObservation = session;
                        await Task.Delay(100, cancellation);
                        continue;
                    }
                }
                worldObservation = null;
                if (characterConfirmed)
                {
                    // Input dispatch is not acknowledgement. The learned pair
                    // can be too early after a slow cold start. Once, return to
                    // freshly recognized menus instead of waiting blindly for
                    // a heartbeat. Exact-name verification still gates entry.
                    if (!confirmationRecoveryUsed && DateTimeOffset.UtcNow - confirmationSentAt >= TimeSpan.FromSeconds(2) && !window.IsCharacterWindow(account.Character))
                    {
                        confirmationRecoveryUsed = true;
                        characterConfirmed = false;
                        selectedCharacterVerified = false;
                        characterConfirmAttempts = 0;
                        replayPath = null;
                        previous = LoginCheckpoint.Unknown;
                        stageSince = DateTimeOffset.UtcNow;
                        report("Checking whether the character Enter was accepted; recovering through verified menus…");
                    }
                    else { await Task.Delay(100, cancellation); continue; }
                }
                if (characterCancelled)
                {
                    if (DateTimeOffset.UtcNow - characterCancelAt > TimeSpan.FromSeconds(3))
                        throw new LoginSafetyException("The character dialog did not finish cancelling. No world entry or further input sent.");
                    dpi = WindowsLoginWindow.SetThreadDpiAwarenessContext(new(-4));
                    bool stillVisible;
                    try
                    {
                        window.Capture(framePath);
                        stillVisible = confirmations.MatchCharacterConfirmation(framePath) != null || confirmations.ObservedConfirmationStillVisible(framePath);
                    }
                    finally { if (dpi != IntPtr.Zero) WindowsLoginWindow.SetThreadDpiAwarenessContext(dpi); }
                    if (stillVisible) { await Task.Delay(50, cancellation); continue; }
                }
                if (characterConfirmAttempts > 0 && confirmations.HasReplay && !characterCancelled &&
                    (trainingSection == "character" || trainingSection == "" && identityVerified && passwordEntered))
                {
                    dpi = WindowsLoginWindow.SetThreadDpiAwarenessContext(new(-4));
                    var matched = false;
                    try
                    {
                        window.Capture(framePath);
                        if (confirmations.MatchCharacterConfirmation(framePath) is {} cached)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            if (trainingSection == "character")
                            {
                                window.Click(cached.CancelX!.Value, cached.CancelY!.Value, cached.Width, cached.Height, gamePressMs);
                                characterCancelled = true;
                                characterCancelAt = DateTimeOffset.UtcNow;
                                characterConfirmAttempts = 0;
                                previous = LoginCheckpoint.CharacterConfirmation;
                                stageSince = DateTimeOffset.UtcNow;
                                report("Matched the cached exact-character prompt; cancelling without OCR…");
                            }
                            else
                            {
                                selectedCharacterVerified = true;
                                window.ConfirmGameSelection(gamePressMs); characterConfirmed = true; confirmationSentAt = DateTimeOffset.UtcNow;
                                report("Matched the cached exact-character prompt; waiting for the configured character/world heartbeat…");
                            }
                            matched = true;
                            RecordAction(LoginCheckpoint.CharacterConfirmation);
                        }
                    }
                    finally { if (dpi != IntPtr.Zero) WindowsLoginWindow.SetThreadDpiAwarenessContext(dpi); }
                    if (matched) { await Task.Delay(characterPollMs, cancellation); continue; }
                    // Match on every capture while the dialog animates. Avoid
                    // starting a blocking OCR read after only three cheap misses;
                    // retain periodic discovery and the same two-second bound.
                    if (++confirmationPolls % 12 != 0 && DateTimeOffset.UtcNow - lastCharacterSelection < TimeSpan.FromSeconds(2))
                    { await Task.Delay(characterPollMs, cancellation); continue; }
                }
                if (trainingSection != "continue" && previous == LoginCheckpoint.Title && replayPath != null && characterConfirmAttempts == 0)
                {
                    dpi = WindowsLoginWindow.SetThreadDpiAwarenessContext(new(-4));
                    try
                    {
                        if (!window.ReplayViewportMatches(replayPath.Width, replayPath.Height))
                        {
                            timing.InvalidateReplayPath(); replayPath = null;
                            report("Window size changed; rediscovering the character path…");
                        }
                        else if (DateTimeOffset.UtcNow - titleEnteredAt >= replayPath.Delay)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            if (CanReplaySuccessfulDefault()) ReplaySuccessfulDefault();
                            else window.ConfirmGameSelection(gamePressMs);
                            characterOpenedThisTrial = trainingSection == "character";
                            characterConfirmAttempts = characterConfirmed ? 0 : 1;
                            lastCharacterSelection = DateTimeOffset.UtcNow;
                            replayUsed = true;
                            if (!characterConfirmed) RecordAction(LoginCheckpoint.Characters);
                            if (!characterConfirmed) report("Replayed cached default-character Enter; verifying the final character name…");
                        }
                    }
                    finally { if (dpi != IntPtr.Zero) WindowsLoginWindow.SetThreadDpiAwarenessContext(dpi); }
                    // A learned delay is a fallback, not a minimum wait. Keep
                    // looking for the partial footer so an earlier ready list
                    // can open its confirmation immediately.
                    loadingPollUntil = DateTimeOffset.UtcNow;
                }
                if (characterConfirmed) continue;
                if (DateTimeOffset.UtcNow < loadingPollUntil)
                {
                    await Task.Delay(50, cancellation); continue;
                }
                LoginFrame frame;
                try
                {
                    // Fast reads only during reversible game-loading stages.
                    // Every third poll retains all fallback crops, and any
                    // newly detected screen is reread fully before input.
                    var loadingOnly = resumedMenu == null && (characterCancelled || characterConfirmAttempts == 0 &&
                        (trainingSection == "character" || previous is LoginCheckpoint.Agreement or LoginCheckpoint.Title or LoginCheckpoint.PlayConfirmation ||
                         previous == LoginCheckpoint.Unknown && window.IsGameWindow) && ++loadingReads % 3 != 0);
                    frame = resumedMenu ?? await ReadFrame(cancellation, loadingOnly, characterConfirmAttempts > 0);
                    resumedMenu = null;
                    if (loadingOnly && LoginCheckpoints.Recognize(frame) != LoginCheckpoint.Unknown &&
                        !((previous is LoginCheckpoint.Title or LoginCheckpoint.Unknown || characterCancelled || trainingSection == "character") && LoginCheckpoints.CanOpenDefaultCharacterConfirmation(frame)) &&
                        !(window.IsGameWindow && LoginCheckpoints.CanOpenCharacterList(frame)))
                        frame = await ReadFrame(cancellation);
                }
                catch (LoginWindowUnavailableException) when (previous != LoginCheckpoint.Unknown && DateTimeOffset.UtcNow - stageSince < TimeSpan.FromSeconds(30))
                {
                    report("Waiting for the owned POL/FFXI window transition…");
                    // A disappearing client is a wait state, not permission to
                    // send input or switch accounts. Revalidate its exact process.
                    await window.WaitForWindowAsync(cancellation);
                    if (!gameHandoffFocused && (previous is LoginCheckpoint.MemberList or LoginCheckpoint.MemberInformation or LoginCheckpoint.Connect or LoginCheckpoint.PasswordEditor or LoginCheckpoint.Home or LoginCheckpoint.Play or LoginCheckpoint.PlayConfirmation) && window.IsGameWindow)
                    {
                        await window.WaitForGameWindowAsync(cancellation);
                        dpi = WindowsLoginWindow.SetThreadDpiAwarenessContext(new(-4));
                        try { window.Focus(); } finally { if (dpi != IntPtr.Zero) WindowsLoginWindow.SetThreadDpiAwarenessContext(dpi); }
                        gameHandoffFocused = true;
                        openingGameUntil = DateTimeOffset.UtcNow.AddSeconds(30);
                    }
                    continue;
                }
                catch (LoginObscuredException) when (previous == LoginCheckpoint.Unknown && startupCaptureUntil > DateTimeOffset.UtcNow)
                {
                    // POL may still be presenting its startup window. Wait without
                    // credentials, clicks or keys; sustained occlusion still stops.
                    await Task.Delay(200, cancellation); continue;
                }
                catch (LoginFocusException) when (openingGameUntil > DateTimeOffset.UtcNow)
                {
                    // Startup can briefly change windows again after the first game window appears.
                    // Only wait for capture; never send input or steal focus while it is changing.
                    await Task.Delay(200, cancellation); continue;
                }
                var stage = LoginCheckpoints.Recognize(frame);
                if (trainingSection == "character" && stage == LoginCheckpoint.Unknown &&
                    LoginCheckpoints.ConfirmationCancelPoint(frame, account.Character) != null)
                    stage = LoginCheckpoint.CharacterConfirmation;
                if (trainingSection == "password" && stage == LoginCheckpoint.Connect && !identityVerified && !passwordEntered)
                {
                    await ReturnToMemberListAsync(frame, cancellation);
                    previous = LoginCheckpoint.Unknown;
                    stageSince = DateTimeOffset.UtcNow;
                    continue;
                }
                if (trainingSection == "password" && stage is not (LoginCheckpoint.Unknown or LoginCheckpoint.MemberList or LoginCheckpoint.MemberInformation or LoginCheckpoint.Connect or LoginCheckpoint.PasswordEditor or LoginCheckpoint.Home or LoginCheckpoint.Play or LoginCheckpoint.PlayConfirmation or LoginCheckpoint.Agreement or LoginCheckpoint.Title or LoginCheckpoint.Error) ||
                    trainingSection == "play" && stage is not (LoginCheckpoint.Unknown or LoginCheckpoint.Home or LoginCheckpoint.Play or LoginCheckpoint.Error) ||
                    trainingSection is "continue" or "character" && stage is LoginCheckpoint.MemberList or LoginCheckpoint.MemberInformation or LoginCheckpoint.Connect or LoginCheckpoint.PasswordEditor ||
                    trainingSection == "character" && stage is not (LoginCheckpoint.Unknown or LoginCheckpoint.Title or LoginCheckpoint.Characters or LoginCheckpoint.CharacterConfirmation or LoginCheckpoint.Error))
                    throw new LoginSafetyException("Section left its allowed route. No credentials were retried and no logout was sent.");
                if (trainingSection == "password" && stage is LoginCheckpoint.Home or LoginCheckpoint.Play or LoginCheckpoint.PlayConfirmation or LoginCheckpoint.Agreement or LoginCheckpoint.Title)
                {
                    if (!identityVerified || !passwordEntered || !submitted) throw new LoginSafetyException("Authentication completion lacks this run's credential submission proof.");
                    training.RecordAuthenticated(account.Id, process, accountBinding);
                    MemberSectionResult = $"Password prompt ready in {promptMs} ms; entry took {passwordMs} ms; authenticated {stage} reached in {sectionClock.ElapsedMilliseconds} ms. One credential submission. Session remains open.";
                    return;
                }
                if (trainingSection == "play" && stage == LoginCheckpoint.Play)
                {
                    if (!playOpened)
                    {
                        playOpened = true;
                        playBackAt = DateTimeOffset.UtcNow;
                        report($"Authenticated Home-to-Play reached in {sectionClock.ElapsedMilliseconds} ms; returning through Back without logout…");
                        dpi = WindowsLoginWindow.SetThreadDpiAwarenessContext(new(-4));
                        try { Click(frame, "Back"); window.ParkCursor(); }
                        finally { if (dpi != IntPtr.Zero) WindowsLoginWindow.SetThreadDpiAwarenessContext(dpi); }
                    }
                    if (DateTimeOffset.UtcNow - playBackAt > TimeSpan.FromSeconds(15))
                        throw new LoginSafetyException("Back did not return to authenticated Home. Session preserved; no logout or credential retry sent.");
                    await Task.Delay(50, cancellation); continue;
                }
                if (trainingSection == "play" && stage == LoginCheckpoint.Home && playOpened)
                {
                    MemberSectionResult = "Authenticated Home-to-Play route returned to Home. No credentials entered or logout sent.";
                    return;
                }
                if (memberSectionOnly && stage is not LoginCheckpoint.Unknown and not LoginCheckpoint.MemberList and not LoginCheckpoint.MemberInformation and not LoginCheckpoint.Connect)
                    throw new LoginSafetyException("Member-section training stopped outside its route. No credentials or game input were sent.");
                if (memberSectionOnly && stage == LoginCheckpoint.Connect && (identityVerified || memberSelectedByExactId) && memberSectionClock != null)
                {
                    var elapsed = memberSectionClock.ElapsedMilliseconds;
                    await ReturnToMemberListAsync(frame, cancellation);
                    controls.CommitSuccessfulRun();
                    controls.RecordSectionTrial(pressMs, pollMs, true);
                    MemberSectionResult = $"Member section reached Connect in {elapsed} ms; returned to the preceding Member List. No password entered. Press={pressMs}ms Poll={pollMs}ms KeyboardPath={polEnterPath.Started}.";
                    return;
                }
                if (memberSectionOnly && cachedLogInAt != null && stage == LoginCheckpoint.MemberInformation)
                {
                    // Never mask a rejected test click with a second normal
                    // click. Return to the preceding screen for the next trial.
                    if (DateTimeOffset.UtcNow - cachedLogInAt > TimeSpan.FromSeconds(2))
                    {
                        await ReturnToMemberListAsync(frame, cancellation);
                        controls.RecordSectionTrial(pressMs, pollMs, false);
                        MemberSectionResult = $"Member section test input was not accepted; returned to Member List. Press={pressMs}ms Poll={pollMs}ms. No password entered.";
                        return;
                    }
                    await Task.Delay(pollMs, cancellation);
                    continue;
                }
                if (!memberSectionOnly && cachedLogInAt != null && stage == LoginCheckpoint.MemberInformation)
                {
                    if (DateTimeOffset.UtcNow - cachedLogInAt >= TimeSpan.FromMilliseconds(250))
                    {
                        if (!polEnterPath.TryRetry(stage, passwordEntered))
                            throw new LoginSafetyException("POL Log In did not advance after bounded Enter retries. No credentials submitted.");
                        dpi = WindowsLoginWindow.SetThreadDpiAwarenessContext(new(-4));
                        try { Click(frame, "Log In"); }
                        finally { if (dpi != IntPtr.Zero) WindowsLoginWindow.SetThreadDpiAwarenessContext(dpi); }
                        cachedLogInAt = DateTimeOffset.UtcNow;
                        LogPolPathEvent("RetryLogInClick", stage);
                        report("Retrying the unchanged verified POL Log In control…");
                    }
                    await Task.Delay(pollMs, cancellation);
                    continue;
                }
                if (window.IsGameWindow && characterConfirmAttempts == 0 &&
                    LoginCheckpoints.CanOpenCharacterList(frame)) stage = LoginCheckpoint.Title;
                // Discovery also acts on the first usable partial character UI.
                // No character counter/detail-font read is needed to open its dialog.
                if ((previous == LoginCheckpoint.Title || trainingSection == "character" || previous == LoginCheckpoint.Unknown && window.IsGameWindow) && characterConfirmAttempts == 0 &&
                    LoginCheckpoints.CanOpenDefaultCharacterConfirmation(frame)) stage = LoginCheckpoint.Characters;
                if (trainingSection == "continue" && stage == LoginCheckpoint.Characters ||
                    trainingSection == "character" && characterCancelled && stage == LoginCheckpoint.Characters)
                {
                    if (trainingSection == "character")
                    {
                        confirmations.CommitSuccessfulRun();
                        if (characterOpenedThisTrial) confirmations.RecordSectionTrial(gamePressMs, characterPollMs, true);
                        characterTrialPassed = true;
                    }
                    MemberSectionResult = trainingSection == "continue"
                        ? $"Game menu route reached character selection in {sectionClock.ElapsedMilliseconds} ms. No credentials or logout; session remains open."
                        : $"Configured character confirmation cancelled back to selection in {sectionClock.ElapsedMilliseconds} ms. No world entry, credentials, or logout. Press={gamePressMs}ms Poll={characterPollMs}ms.";
                    return;
                }
                if (stage is LoginCheckpoint.Agreement or LoginCheckpoint.Title or LoginCheckpoint.Characters) openingGameUntil = null;
                if (stage == LoginCheckpoint.Unknown)
                {
                    if (characterConfirmAttempts > 0 && DateTimeOffset.UtcNow - lastCharacterSelection > TimeSpan.FromSeconds(8))
                        throw new LoginSafetyException("The character confirmation did not appear after Enter. Cached replay will be relearned; no further world-entry input was sent.");
                    if (DateTimeOffset.UtcNow - unknownSince > TimeSpan.FromSeconds(45)) throw new LoginSafetyException("Unrecognized login screen. Automation stopped without guessing.");
                    loadingPollUntil = DateTimeOffset.UtcNow + (characterConfirmAttempts > 0 || window.IsGameWindow ? TimeSpan.Zero : timing.LoadingPollDelay(previous, DateTimeOffset.UtcNow - stageSince));
                    await Task.Delay(characterConfirmAttempts > 0 || window.IsGameWindow ? 50 : 150, cancellation); continue;
                }
                unknownSince = DateTimeOffset.UtcNow;
                if (confirmationRecoveryUsed && !characterConfirmed && stage is LoginCheckpoint.Agreement or LoginCheckpoint.Title or LoginCheckpoint.Characters or LoginCheckpoint.CharacterConfirmation)
                    timing.InvalidateReplayPath();
                if (stage == LoginCheckpoint.Error)
                {
                    var rejected = passwordEntered && LoginCheckpoints.IsCredentialRejection(frame);
                    LogPolPathEvent(rejected ? "CredentialRejected" : "PolError", stage);
                    throw new LoginSafetyException(rejected
                        ? "Could not validate user name or password. Please check. Automatic authentication retries stopped."
                        : "POL reported a login error. Automatic retries are paused.");
                }
                if (characterConfirmAttempts > 0 && stage != LoginCheckpoint.CharacterConfirmation && stage != LoginCheckpoint.Characters)
                {
                    if (stage != LoginCheckpoint.Title || DateTimeOffset.UtcNow - lastCharacterSelection > TimeSpan.FromSeconds(8))
                        throw new LoginSafetyException("Cached character opening did not reach its confirmation. Input stopped; the path will be relearned.");
                    await Task.Delay(50, cancellation); continue;
                }
                if (stage == LoginCheckpoint.Characters && characterConfirmAttempts > 0)
                {
                    // The list remains visible behind the confirmation dialog.
                    // A partial OCR read cannot prove that our first Enter was
                    // ignored. Never retry it and accidentally accept an unread
                    // character confirmation; wait for its exact name instead.
                    if (DateTimeOffset.UtcNow - lastCharacterSelection > TimeSpan.FromSeconds(8))
                        throw new LoginSafetyException("The character confirmation could not be read after Enter. Selection was preserved; no further world-entry input was sent.");
                    await Task.Delay(50, cancellation); continue;
                }
                if (stage == previous && stage != LoginCheckpoint.Characters)
                {
                    if (DateTimeOffset.UtcNow - stageSince > TimeSpan.FromSeconds(45)) throw new LoginSafetyException("The last login action did not advance the screen. Inspect POL before retrying.");
                    if (stage == LoginCheckpoint.MemberList && !identityVerified && !passwordEntered && memberSelectionRetries < 3 &&
                        DateTimeOffset.UtcNow - stageSince >= TimeSpan.FromMilliseconds(500 * (memberSelectionRetries + 1)))
                    {
                        var cards = frame.IdentityCandidates(account.SquareEnixId);
                        var label = LoginCheckpoints.MemberCardLabel(frame, account.SquareEnixId);
                        if (cards.Length == 1 && label != null && label.Equals(selectedMemberLabel, StringComparison.OrdinalIgnoreCase))
                        {
                            dpi = WindowsLoginWindow.SetThreadDpiAwarenessContext(new(-4));
                            try
                            {
                                window.Key(0x0d);
                            }
                            finally { if (dpi != IntPtr.Zero) WindowsLoginWindow.SetThreadDpiAwarenessContext(dpi); }
                            memberSelectionRetries++;
                            report("Retrying the unchanged exact-ID POL member selection…");
                            await Task.Delay(50, cancellation); continue;
                        }
                    }
                    if (stage == LoginCheckpoint.Connect && identityVerified && editorOpened && !passwordEntered &&
                        DateTimeOffset.UtcNow - stageSince >= TimeSpan.FromSeconds(3))
                        throw new LoginSafetyException("The password editor did not open. No password was entered; inspect POL before retrying.");
                    // Once editor opening is requested, reread readiness only.
                    // A stale Connect frame cannot justify more input into the software keyboard.
                    // FFXI can expose a menu before its first Enter is accepted.
                    // Retry only freshly recognized reversible menu transitions,
                    // never credentials or the final character confirmation.
                    if ((stage is LoginCheckpoint.Play or LoginCheckpoint.PlayConfirmation or LoginCheckpoint.Agreement or LoginCheckpoint.Title) &&
                        DateTimeOffset.UtcNow - stageSince >= TimeSpan.FromMilliseconds(500) && enterRetries.GetValueOrDefault(stage) < 3)
                    {
                        dpi = WindowsLoginWindow.SetThreadDpiAwarenessContext(new(-4));
                        try { window.ConfirmGameSelection(); }
                        finally { if (dpi != IntPtr.Zero) WindowsLoginWindow.SetThreadDpiAwarenessContext(dpi); }
                        enterRetries[stage] = enterRetries.GetValueOrDefault(stage) + 1;
                        if (stage == LoginCheckpoint.Title) titleEnteredAt = DateTimeOffset.UtcNow;
                        report("Retrying Enter on the unchanged " + stage + " screen…");
                        await Task.Delay(50, cancellation); continue;
                    }
                    await Task.Delay(150, cancellation); continue;
                }
                if (stage != previous)
                {
                    timing.Observe(previous, DateTimeOffset.UtcNow - stageSince);
                    stageSince = DateTimeOffset.UtcNow;
                }
                previous = stage;
                visits[stage] = visits.GetValueOrDefault(stage) + 1;
                if (visits[stage] > 2 && stage is not LoginCheckpoint.Characters and not LoginCheckpoint.CharacterConfirmation) throw new LoginSafetyException("A login screen repeated unexpectedly. Automation stopped.");
                cancellation.ThrowIfCancellationRequested();
                report(stage switch { LoginCheckpoint.MemberList => "Selecting the saved POL member…", LoginCheckpoint.MemberInformation => memberSelectedByExactId ? "Following the verified POL Enter path…" : "Returning to the member list for exact-ID selection…", LoginCheckpoint.Connect => passwordEntered ? "Connecting to PlayOnline…" : "Opening the Square Enix password field…", LoginCheckpoint.PasswordEditor => "Entering the saved Square Enix password…", LoginCheckpoint.Title => "Selecting the configured character…", _ => "Login checkpoint: " + stage });
                if (stage == LoginCheckpoint.CharacterConfirmation)
                {
                    // A clear exact prompt is sufficient to confirm immediately.
                    // Animation/crop noise waits without navigation or input;
                    // names in the list behind the dialog never authorize entry.
                    var stableUntil = DateTimeOffset.UtcNow.AddSeconds(8);
                    while (!LoginCheckpoints.ConfirmsCharacter(frame, account.Character) && DateTimeOffset.UtcNow < stableUntil)
                    {
                        await Task.Delay(50, cancellation);
                        frame = await ReadFrame(cancellation, confirmationOnly: true);
                        var current = LoginCheckpoints.Recognize(frame);
                        if (current == LoginCheckpoint.Error) throw new LoginSafetyException("POL reported a login error. Automatic retries are paused.");
                        if (current is not LoginCheckpoint.Unknown and not LoginCheckpoint.CharacterConfirmation)
                            throw new LoginSafetyException("The character confirmation changed before input. Input stopped.");
                    }
                    if (!LoginCheckpoints.ConfirmsCharacter(frame, account.Character))
                        throw new LoginSafetyException("The selected character confirmation could not be matched exactly. Selection was preserved; no world-entry input was sent.");
                }
                if (stage == LoginCheckpoint.MemberList && frame.IdentityCandidates(account.SquareEnixId).Length != 1)
                {
                    var readableUntil = DateTimeOffset.UtcNow.AddSeconds(5);
                    while ((LoginCheckpoints.Recognize(frame) != LoginCheckpoint.MemberList || frame.IdentityCandidates(account.SquareEnixId).Length != 1) && DateTimeOffset.UtcNow < readableUntil)
                    {
                        await Task.Delay(50, cancellation);
                        frame = await ReadFrame(cancellation);
                        if (LoginCheckpoints.Recognize(frame) is not LoginCheckpoint.MemberList and not LoginCheckpoint.Unknown)
                            throw new LoginSafetyException("The POL member list changed before identity verification. No password was entered.");
                    }
                }
                if (stage == LoginCheckpoint.MemberList && LoginCheckpoints.MemberCardLabel(frame, account.SquareEnixId) == null)
                {
                    // The ID can render before the card caption. Preserve the
                    // exact-ID/card-label pair before opening member details,
                    // where this label corroborates an otherwise unreadable ID.
                    var captionUntil = DateTimeOffset.UtcNow.AddSeconds(2);
                    while (LoginCheckpoints.MemberCardLabel(frame, account.SquareEnixId) == null && DateTimeOffset.UtcNow < captionUntil)
                    {
                        await Task.Delay(50, cancellation);
                        frame = await ReadFrame(cancellation);
                        if (LoginCheckpoints.Recognize(frame) is not LoginCheckpoint.MemberList and not LoginCheckpoint.Unknown)
                            throw new LoginSafetyException("The POL member card changed before selection. No password was entered.");
                    }
                }
                if (stage == LoginCheckpoint.MemberList && LoginCheckpoints.Recognize(frame) != LoginCheckpoint.MemberList)
                    throw new LoginSafetyException("The POL member card remained unreadable before selection. No password was entered.");
                try
                {
                    switch (stage)
                    {
                        case LoginCheckpoint.MemberList:
                            memberSectionClock ??= Stopwatch.StartNew();
                            var cards = frame.IdentityCandidates(account.SquareEnixId);
                            if (cards.Length != 1) throw new LoginSafetyException("The saved POL member cannot be matched uniquely to the Square Enix ID. Select it manually and retry.");
                            selectedMemberLabels = LoginCheckpoints.MemberCardLabels(frame, account.SquareEnixId);
                            selectedMemberLabel = selectedMemberLabels.FirstOrDefault()
                                ?? throw new LoginSafetyException("The exact-ID member card caption could not be identified. No member-selection input was sent.");
                            // The ID strip identifies the card; its local member
                            // caption is the activation target below that strip.
                            Click(frame, selectedMemberLabel);
                            memberSelectedByExactId = true;
                            // Do not double-advance a card that opens on click.
                            // An unchanged exact-ID list can retry Enter above.
                            break;
                        case LoginCheckpoint.MemberInformation:
                            if (!identityVerified && !memberSelectedByExactId)
                            {
                                if (returnedToMemberList) throw new LoginSafetyException("A fresh exact-ID member selection is required before login.");
                                Click(frame, "Back"); returnedToMemberList = true; visits[stage]--; break;
                            }
                            identityVerified = true;
                            if (!polEnterPath.Started) polEnterPath.Begin(memberSelectedByExactId);
                            window.Key(0x0d);
                            LogPolPathEvent("LogInEnter", stage);
                            cachedLogInAt = DateTimeOffset.UtcNow;
                            break;
                        case LoginCheckpoint.Connect:
                            if (memberSelectedByExactId) identityVerified = true;
                            if (memberSectionOnly && identityVerified)
                                throw new LoginSafetyException("Member-section training reached its boundary without a recorded selection; input stopped.");
                            if (!identityVerified)
                            {
                                // A previous stopped attempt may leave this dialog open.
                                // Return to the member information to establish identity again.
                                if (returnedForIdentity) throw new LoginSafetyException("Could not return to the member information to verify the Square Enix ID.");
                                Click(frame, "Cancel"); returnedForIdentity = true; visits[stage]--; break;
                            }
                            if (passwordEntered)
                            {
                                if (submitted) throw new LoginSafetyException("A credential submission was already attempted; automatic resubmission is blocked.");
                                cancellation.ThrowIfCancellationRequested();
                                submitted = true;
                                Click(frame, "Connect");
                                LogPolPathEvent("SubmitCredentialsClick", stage);
                            }
                            else
                            {
                                var field = LoginCheckpoints.PasswordField(frame) ?? throw new LoginSafetyException("The Square Enix password field could not be located safely.");
                                window.Click(field.X, field.Y, frame.Width, frame.Height); editorOpened = true;
                                LogPolPathEvent("OpenPasswordFieldClick", stage);
                            }
                            break;
                        case LoginCheckpoint.PasswordEditor:
                            if (!identityVerified || !editorOpened || passwordEntered) throw new LoginSafetyException("Unexpected password editor. Credential entry stopped.");
                            if (trainingSection == "password")
                            {
                                promptMs = sectionClock.ElapsedMilliseconds;
                                if (!training.ReserveCredentialEntry(account.Id)) throw new LoginSafetyException("This account already used its one training credential entry. Automatic retries are blocked; the session is preserved.");
                            }
                            var credentials = vault.Read(account.Id) ?? throw new LoginSafetyException("Save the Square Enix password before automatic login.");
                            if (string.IsNullOrEmpty(credentials.SquareEnixPassword)) throw new LoginSafetyException("The saved Square Enix password is empty.");
                            var entryClock = Stopwatch.StartNew();
                            LogPolPathEvent("PasswordEntryStarted", stage);
                            window.Password(credentials.SquareEnixPassword, cancellation);
                            cancellation.ThrowIfCancellationRequested();
                            window.Key(0x0d); passwordEntered = true;
                            LogPolPathEvent("PasswordEditorCommitEnter", stage);
                            passwordMs = entryClock.ElapsedMilliseconds; break;
                        case LoginCheckpoint.Home: Click(frame, frame.UniqueLine("FINAL FANTASY XI") != null ? "FINAL FANTASY XI" : "FINAL FANTASY"); break;
                        case LoginCheckpoint.Play: window.ConfirmGameSelection(); break;
                        case LoginCheckpoint.PlayConfirmation: window.ConfirmGameSelection(); break;
                        case LoginCheckpoint.Agreement: window.ConfirmGameSelection(); break;
                        case LoginCheckpoint.Title:
                            window.ConfirmGameSelection();
                            titleEnteredAt = DateTimeOffset.UtcNow;
                            replayPath = timing.ReplayPath;
                            if (replayPath != null) report($"Using cached Title → default-character Enter path ({replayPath.Delay.TotalMilliseconds:0} ms); loading OCR skipped.");
                            break;
                        case LoginCheckpoint.Characters:
                            if (characterConfirmed) break;
                            if (!replayUsed && titleEnteredAt != DateTimeOffset.MinValue)
                                timing.ObserveCharacterPath(frame.Width, frame.Height, DateTimeOffset.UtcNow - titleEnteredAt);
                            // Discovery opens the confirmation for an exact-name
                            // check. A previously proven default path can replay
                            // the successful pair before the dialog finishes drawing.
                            if (++characterConfirmAttempts > 3) throw new LoginSafetyException("The game did not open the character confirmation. Input stopped.");
                            if (CanReplaySuccessfulDefault() && window.ReplayViewportMatches(replayPath!.Width, replayPath.Height))
                                ReplaySuccessfulDefault();
                            else window.ConfirmGameSelection(gamePressMs);
                            characterOpenedThisTrial = trainingSection == "character";
                            lastCharacterSelection = DateTimeOffset.UtcNow;
                            break;
                        case LoginCheckpoint.CharacterConfirmation:
                            characterConfirmAttempts = 0;
                            if (visits[stage] > 17) throw new LoginSafetyException("The configured character was not found in the character list.");
                            if (!LoginCheckpoints.ConfirmsCharacter(frame, account.Character))
                            {
                                // An OCR mismatch is not permission to move away
                                // from the default character. Preserve selection.
                                throw new LoginSafetyException("The selected character confirmation does not match the configured character. Selection was preserved; no world-entry input was sent.");
                            }
                            if (trainingSection is "" or "character") confirmations.ObserveCharacterConfirmation(frame, framePath, account.Character);
                            if (trainingSection == "character")
                            {
                                var cancelPoint = LoginCheckpoints.ConfirmationCancelPoint(frame, account.Character) ??
                                    throw new LoginSafetyException("The verified confirmation's cancel point could not be located safely. No world entry sent.");
                                window.Click(cancelPoint.X, cancelPoint.Y, frame.Width, frame.Height, gamePressMs);
                                characterCancelled = true; characterConfirmAttempts = 0;
                                characterCancelAt = DateTimeOffset.UtcNow;
                                break;
                            }
                            selectedCharacterVerified = true;
                            window.ConfirmGameSelection(gamePressMs); characterConfirmed = true; confirmationSentAt = DateTimeOffset.UtcNow;
                            report("Waiting for the configured character/world addon heartbeat…"); break;
                        default: throw new LoginSafetyException("Unsupported login checkpoint.");
                    }
                }
                finally { if (dpi != IntPtr.Zero) WindowsLoginWindow.SetThreadDpiAwarenessContext(dpi); }
                if (!(stage == LoginCheckpoint.Characters && characterConfirmed && replayUsed)) RecordAction(stage);
                if (stage == LoginCheckpoint.PlayConfirmation)
                {
                    report("Waiting for POL to open the FFXI game window…");
                    openingGameUntil = DateTimeOffset.UtcNow.AddSeconds(30);
                    await window.WaitForGameWindowAsync(cancellation);
                    dpi = WindowsLoginWindow.SetThreadDpiAwarenessContext(new(-4));
                    try { window.Focus(); } finally { if (dpi != IntPtr.Zero) WindowsLoginWindow.SetThreadDpiAwarenessContext(dpi); }
                    gameHandoffFocused = true;
                }
                // The next fresh capture establishes readiness; avoid imposing
                // another long pause on every already-rendered screen.
                await Task.Delay(50, cancellation);
            }
            throw new LoginSafetyException("Login timed out before the configured character and world were verified online.");
        }
        finally
        {
            if (trainingSection == "character" && !characterTrialPassed)
            {
                try { confirmations.RecordSectionTrial(gamePressMs, characterPollMs, false); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            if (replayUsed && !worldVerified && !characterTrialPassed)
            {
                try { timing.InvalidateReplayPath(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            if (File.Exists(framePath)) File.Delete(framePath);
        }
    }

    private async Task ReturnToMemberListAsync(LoginFrame frame, CancellationToken cancellation)
    {
        // POL's highlighted Cancel caption can disappear from OCR. Remove
        // hover before reset discovery, then use a fresh frame for input.
        var initialDpi = WindowsLoginWindow.SetThreadDpiAwarenessContext(new(-4));
        try { window.ParkCursor(); }
        finally { if (initialDpi != IntPtr.Zero) WindowsLoginWindow.SetThreadDpiAwarenessContext(initialDpi); }
        frame = await ReadFrame(cancellation);
        var until = DateTimeOffset.UtcNow.AddSeconds(15);
        var cancelAttempts = 0;
        var backAttempts = 0;
        var lastInput = DateTimeOffset.MinValue;
        while (DateTimeOffset.UtcNow < until)
        {
            cancellation.ThrowIfCancellationRequested();
            var stage = LoginCheckpoints.Recognize(frame);
            if (stage == LoginCheckpoint.MemberList && frame.IdentityCandidates(account.SquareEnixId).Length == 1) return;
            var dpi = WindowsLoginWindow.SetThreadDpiAwarenessContext(new(-4));
            try
            {
                if (DateTimeOffset.UtcNow - lastInput >= TimeSpan.FromMilliseconds(300))
                {
                    if (stage == LoginCheckpoint.Connect && cancelAttempts < 3) { Click(frame, "Cancel"); window.ParkCursor(); cancelAttempts++; lastInput = DateTimeOffset.UtcNow; }
                    else if (stage == LoginCheckpoint.MemberInformation && backAttempts < 3) { Click(frame, "Back"); window.ParkCursor(); backAttempts++; lastInput = DateTimeOffset.UtcNow; }
                }
                if (stage is not LoginCheckpoint.Unknown and not LoginCheckpoint.MemberList and not LoginCheckpoint.MemberInformation and not LoginCheckpoint.Connect)
                    throw new LoginSafetyException("Section reset left its known route; input stopped.");
            }
            finally { if (dpi != IntPtr.Zero) WindowsLoginWindow.SetThreadDpiAwarenessContext(dpi); }
            await Task.Delay(50, cancellation);
            frame = await ReadFrame(cancellation);
        }
        throw new LoginSafetyException("Section reset did not return to the exact-ID Member List; no further input was sent.");
    }
}
