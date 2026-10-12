using FFXILauncher.Core;

var passed = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception("FAIL: " + label);
    passed++;
    Console.WriteLine("PASS: " + label);
}
var trainingDirectory = Path.Combine(Path.GetTempPath(), "ffxi-training-" + Guid.NewGuid());
try
{
    var ledger = new LoginTrainingLedger(trainingDirectory);
    var trainingAccount = Guid.NewGuid();
    var trainingProcess = new ManagedProcess(123, DateTime.UtcNow);
    var binding = LoginTrainingLedger.IdentityBinding("test-identity");
    Check(!ledger.MatchesAuthenticated(trainingAccount, trainingProcess, binding), "training cannot resume without authentication evidence");
    Check(ledger.ReserveCredentialEntry(trainingAccount), "first training credential entry is reserved");
    Check(ledger.CredentialEntryUsed(trainingAccount) && !ledger.MatchesAuthenticated(trainingAccount, trainingProcess, binding), "spent credential entry is not authentication proof");
    Check(!ledger.ReserveCredentialEntry(trainingAccount), "second credential entry is blocked");
    Check(!new LoginTrainingLedger(trainingDirectory).ReserveCredentialEntry(trainingAccount), "credential limit survives helper restart");
    ledger.RecordAuthenticated(trainingAccount, trainingProcess, binding);
    Check(ledger.MatchesAuthenticated(trainingAccount, trainingProcess, binding), "authenticated training resumes exact account and process");
    Check(!ledger.MatchesAuthenticated(trainingAccount, trainingProcess with { StartedUtc = trainingProcess.StartedUtc.AddSeconds(1) }, binding), "PID reuse cannot inherit training authentication");
    Check(!ledger.MatchesAuthenticated(Guid.NewGuid(), trainingProcess, binding), "another account cannot inherit training authentication");
    Check(!ledger.MatchesAuthenticated(trainingAccount, trainingProcess, LoginTrainingLedger.IdentityBinding("changed")), "changed account identity invalidates training session");
    File.WriteAllText(Path.Combine(trainingDirectory, trainingAccount.ToString("N") + ".session.json"), "broken");
    Check(!ledger.MatchesAuthenticated(trainingAccount, trainingProcess, binding), "corrupt training proof fails closed");
}
finally { if (Directory.Exists(trainingDirectory)) Directory.Delete(trainingDirectory, true); }
var workflow = new LoginWorkflow("Hauplibond", "Odin");
Check(!workflow.Observe(new(LoginStage.Online, true, "Hauplibond", "Odin")), "cannot skip login checkpoints");
Check(!workflow.Observe(new(LoginStage.PolMember, false)), "unknown screen never advances");
foreach (var stage in new[] { LoginStage.PolMember, LoginStage.Authentication, LoginStage.FfxiTitle, LoginStage.CharacterSelection })
    Check(workflow.Observe(new(stage, true)), "ordered transition: " + stage);
Check(!workflow.Observe(new(LoginStage.WorldEntry, true, "Wrong", "Odin")), "wrong character rejected");
Check(!workflow.Observe(new(LoginStage.WorldEntry, true, "Hauplibond", "Wrong")), "wrong world rejected");
Check(workflow.Observe(new(LoginStage.WorldEntry, true, "Hauplibond", "Odin")), "correct world entry accepted");
Check(workflow.Stage != LoginStage.Online, "character confirmation alone is not online");
Check(workflow.Observe(new(LoginStage.Online, true, "Hauplibond", "Odin")), "verified identity reaches online");
workflow.Pause();
Check(!workflow.Observe(new(LoginStage.Online, true, "Hauplibond", "Odin")), "paused workflow cannot resume itself");
Check(new Viewport(-1920, 40, 1280, 720).Map(960, 540, 1920, 1080) == (-1280, 400), "scaled viewport includes monitor offset");
try { new Viewport(0, 0, 0, 720).Map(0, 0, 1920, 1080); throw new Exception("invalid geometry accepted"); }
catch (ArgumentOutOfRangeException) { passed++; }
var directory = Path.Combine(Path.GetTempPath(), "ffxi-tests-" + Guid.NewGuid());
try
{
    var store = new ConfigurationStore(directory);
    Check(store.Load().ConfiguredAccounts.Count == 0, "fresh configuration has no accounts");
    var account = new AccountConfiguration(Guid.NewGuid(), "Main", "Main profile", "Hauplibond", "Odin", "Member", "SE account", true);
    store.Save(new("Windower.exe", "pol.exe", [account]));
    Check(store.Load().ConfiguredAccounts.Single() == account, "account settings round trip");
    Check(!File.ReadAllText(Path.Combine(directory, "accounts.json")).Contains("\"PolPassword\"") && !File.ReadAllText(Path.Combine(directory, "accounts.json")).Contains("\"SquareEnixPassword\"") && !File.ReadAllText(Path.Combine(directory, "accounts.json")).Contains("\"MemberPassword\""), "configuration contains no credential fields");
    store.Save(new("", "", [account, account]));
    try { store.Load(); throw new Exception("duplicate identities accepted"); }
    catch (InvalidDataException) { passed++; }
    var sessionStore = new ManagedSessionStore(directory);
    var managed = new ManagedSession(account.Id, new ManagedProcess(123, DateTime.UtcNow), "C:\\Games\\pol.exe");
    sessionStore.Save([managed]);
    var restored = new ManagedSessionStore(directory).Load().Single();
    Check(restored == managed && restored.Matches(managed.Process, "c:\\games\\POL.exe"), "managed POL ownership survives a new store instance and matches the verified process");
    Check(!restored.Matches(managed.Process with { StartedUtc = managed.Process.StartedUtc.AddSeconds(1) }, managed.PolPath), "reused PID cannot restore a previous account's session");
    Check(!restored.Matches(managed.Process, "C:\\Other\\pol.exe"), "different executable cannot restore a managed session");
    try { sessionStore.Save([managed, managed with { AccountId = Guid.NewGuid() }]); throw new Exception("duplicate process ownership accepted"); }
    catch (InvalidDataException) { passed++; }
    Check(sessionStore.Load().Single() == managed, "invalid ownership update preserves the previous session mapping");
    var corruptSessions = "[{invalid]";
    File.WriteAllText(Path.Combine(directory, "managed-sessions.json"), corruptSessions);
    try { sessionStore.Load(); throw new Exception("corrupt session mappings accepted"); }
    catch (System.Text.Json.JsonException) { passed++; }
    Check(File.ReadAllText(Path.Combine(directory, "managed-sessions.json")) == corruptSessions, "corrupt session mappings are preserved for diagnosis");
    var timingsDirectory = Path.Combine(directory, "login-timing");
    var profileSettingsPath = Path.Combine(directory, "settings.xml");
    File.WriteAllText(profileSettingsPath, "<settings><profile name=\"Low Res\"><width>1280</width></profile><profile name=\"Other\"><width>800</width></profile></settings>");
    var settingsBinding = LoginTimingCache.ProfileSettingsBinding(profileSettingsPath, "Low Res");
    File.WriteAllText(profileSettingsPath, "<settings>\n<profile name=\"Low Res\">\n<width>1280</width>\n</profile><profile name=\"Other\"><width>1024</width></profile></settings>");
    File.SetLastWriteTimeUtc(profileSettingsPath, DateTime.UtcNow.AddSeconds(2));
    Check(LoginTimingCache.ProfileSettingsBinding(profileSettingsPath, "Low Res") == settingsBinding,
        "settings rewrite, whitespace and unrelated profiles preserve selected-profile cache binding");
    File.WriteAllText(profileSettingsPath, "<settings><profile name=\"Low Res\"><width>1920</width></profile></settings>");
    Check(LoginTimingCache.ProfileSettingsBinding(profileSettingsPath, "Low Res") != settingsBinding,
        "changed selected-profile resolution invalidates timing hints");
    var timings = new LoginTimingCache(timingsDirectory, "account-profile-A");
    timings.Observe(LoginCheckpoint.Title, TimeSpan.FromSeconds(10));
    Check(new LoginTimingCache(timingsDirectory, "account-profile-A").LoadingPollDelay(LoginCheckpoint.Title, TimeSpan.Zero) == TimeSpan.Zero,
        "uncommitted failed login cannot teach timing hints");
    timings.CommitSuccessfulRun();
    Check(new LoginTimingCache(timingsDirectory, "account-profile-A").LoadingPollDelay(LoginCheckpoint.Title, TimeSpan.Zero) == TimeSpan.Zero,
        "one successful sample does not suppress loading reads");
    timings = new LoginTimingCache(timingsDirectory, "account-profile-A");
    timings.Observe(LoginCheckpoint.Title, TimeSpan.FromSeconds(6));
    timings.CommitSuccessfulRun();
    timings = new LoginTimingCache(timingsDirectory, "account-profile-A");
    Check(timings.LoadingPollDelay(LoginCheckpoint.Title, TimeSpan.FromSeconds(2)) == TimeSpan.FromSeconds(1),
        "learned timing survives restart with bounded one-second loading polling");
    Check(timings.LoadingPollDelay(LoginCheckpoint.Title, TimeSpan.FromSeconds(4)) == TimeSpan.Zero,
        "normal reads resume two seconds before fastest learned transition");
    Check(timings.LoadingPollDelay(LoginCheckpoint.CharacterConfirmation, TimeSpan.Zero) == TimeSpan.Zero &&
        timings.LoadingPollDelay(LoginCheckpoint.PasswordEditor, TimeSpan.Zero) == TimeSpan.Zero,
        "timing cache never suppresses credential or character-confirmation reads");
    Check(new LoginTimingCache(timingsDirectory, "account-profile-B").LoadingPollDelay(LoginCheckpoint.Title, TimeSpan.Zero) == TimeSpan.Zero,
        "changed profile or identity cannot reuse old timing hints");
    Check(timings.ReplayPath == null, "old polling hints do not authorize cached action replay");
    timings.ObserveCharacterPath(1280, 800, TimeSpan.FromMilliseconds(1500));
    Check(new LoginTimingCache(timingsDirectory, "account-profile-A").ReplayPath == null, "uncommitted path is not reusable");
    timings.CommitSuccessfulRun();
    timings = new LoginTimingCache(timingsDirectory, "account-profile-A");
    Check(timings.ReplayPath == null, "one full login does not activate action replay");
    timings.ObserveCharacterPath(1280, 800, TimeSpan.FromMilliseconds(1700));
    timings.CommitSuccessfulRun();
    timings = new LoginTimingCache(timingsDirectory, "account-profile-A");
    Check(timings.ReplayPath is { Width: 1280, Height: 800 } && timings.ReplayPath.Delay == TimeSpan.FromMilliseconds(1800),
        "validated Enter path survives restart with slowest observed readiness plus 100ms");
    Check(new LoginTimingCache(timingsDirectory, "account-profile-B").ReplayPath == null, "different binding cannot replay another character path");
    timings.InvalidateReplayPath();
    Check(new LoginTimingCache(timingsDirectory, "account-profile-A").ReplayPath == null, "failed replay disables persisted path before rediscovery");
    timings.ObserveCharacterPath(1280, 800, TimeSpan.FromMilliseconds(1500)); timings.CommitSuccessfulRun();
    timings = new LoginTimingCache(timingsDirectory, "account-profile-A");
    timings.ObserveCharacterPath(1920, 1080, TimeSpan.FromMilliseconds(1500)); timings.CommitSuccessfulRun();
    Check(new LoginTimingCache(timingsDirectory, "account-profile-A").ReplayPath == null, "changed viewport starts training again");
    File.WriteAllText(Directory.GetFiles(timingsDirectory).Single(), "{broken");
    Check(new LoginTimingCache(timingsDirectory, "account-profile-A").LoadingPollDelay(LoginCheckpoint.Title, TimeSpan.Zero) == TimeSpan.Zero,
        "corrupt timing cache falls back to normal reads");
    var controlsDirectory = Path.Combine(directory, "login-controls");
    var bmpPath = Path.Combine(directory, "control.bmp");
    var pixels = new byte[54 + 640 * 480 * 4];
    BitConverter.GetBytes((ushort)0x4d42).CopyTo(pixels, 0);
    BitConverter.GetBytes(54).CopyTo(pixels, 10); BitConverter.GetBytes(40).CopyTo(pixels, 14);
    BitConverter.GetBytes(640).CopyTo(pixels, 18); BitConverter.GetBytes(-480).CopyTo(pixels, 22);
    BitConverter.GetBytes((ushort)1).CopyTo(pixels, 26); BitConverter.GetBytes((ushort)32).CopyTo(pixels, 28);
    for (var i = 54; i < pixels.Length; i++) pixels[i] = (byte)(i % 251);
    File.WriteAllBytes(bmpPath, pixels);
    var buttonFrame = new LoginFrame(640, 480, [new("Log In", [new("Log In", 400, 400, 40, 12)])]);
    var controls = new LoginControlCache(controlsDirectory, "shared-POL-layout");
    controls.ObserveLogIn(buttonFrame, bmpPath);
    Check(!new LoginControlCache(controlsDirectory, "shared-POL-layout").HasReplay, "failed run cannot teach a shared control");
    controls.CommitSuccessfulRun();
    controls = new LoginControlCache(controlsDirectory, "shared-POL-layout");
    Check(!controls.HasReplay, "one successful control observation does not activate replay");
    controls.ObserveLogIn(buttonFrame, bmpPath); controls.CommitSuccessfulRun();
    controls = new LoginControlCache(controlsDirectory, "shared-POL-layout");
    Check(controls.HasReplay && controls.MatchLogIn(bmpPath) != null, "shared Log In geometry and exact pixel digest survive restart after two successful logins");
    pixels[54 + (405 * 640 + 410) * 4] ^= 1; File.WriteAllBytes(bmpPath, pixels);
    Check(controls.MatchLogIn(bmpPath) == null, "changed control pixels reject cached click instead of guessing readiness");
    pixels[54 + (405 * 640 + 410) * 4] ^= 1; pixels[54] ^= 1; File.WriteAllBytes(bmpPath, pixels);
    Check(controls.MatchLogIn(bmpPath) != null, "other account text and screen content outside Log In do not invalidate the shared path");
    pixels[54 + (405 * 640 + 410) * 4 + 3] ^= 1; File.WriteAllBytes(bmpPath, pixels);
    Check(controls.MatchLogIn(bmpPath) != null, "unspecified GDI alpha is excluded from the shared control fingerprint");
    BitConverter.GetBytes(108).CopyTo(pixels, 10); File.WriteAllBytes(bmpPath, pixels);
    Check(controls.MatchLogIn(bmpPath) == null, "malformed bitmap offsets cannot authorize cached clicks");
    Check(!new LoginControlCache(controlsDirectory, "different-POL-version").HasReplay, "changed POL installation cannot reuse shared controls");
    var confirmationDirectory = Path.Combine(directory, "character-confirmations");
    var promptBitmap = Path.Combine(directory, "confirmation.bmp");
    var promptPixels = (byte[])pixels.Clone();
    BitConverter.GetBytes(54).CopyTo(promptPixels, 10); File.WriteAllBytes(promptBitmap, promptPixels);
    var promptFrame = new LoginFrame(640, 480, [new("Log in with 'Mainchar'?", [new("Log", 200, 210, 30, 14), new("in", 236, 210, 15, 14), new("with", 260, 210, 30, 14), new("'Mainchar'?", 300, 210, 80, 14)]), new("No. of content IDs purchased: 1", []), new("Yes", [new("Yes", 350, 251, 30, 14)])]);
    var prompts = new LoginControlCache(confirmationDirectory, "account-A-profile-A");
    prompts.ObserveCharacterConfirmation(promptFrame, promptBitmap, "Otherchar"); prompts.CommitSuccessfulRun();
    Check(!prompts.HasReplay, "wrong-character prompt cannot teach a pixel confirmation route");
    var shiftedPromptBoxes = promptFrame with { Lines = promptFrame.Lines.Select(l => l.Text.StartsWith("Log in with") ? l with { Words = l.Words.Select(w => w with { X = w.X + 1, Y = w.Y - 2, Height = w.Height + 2 }).ToArray() } : l).ToArray() };
    prompts.ObserveCharacterConfirmation(shiftedPromptBoxes, promptBitmap, "Mainchar"); prompts.CommitSuccessfulRun();
    Check(!prompts.HasReplay, "one reversible return does not activate character prompt replay");
    prompts.ObserveCharacterConfirmation(promptFrame, promptBitmap, "Mainchar");
    Check(prompts.ObservedConfirmationStillVisible(promptBitmap), "this trial's verified prompt can detect an unchanged cancel dialog without authorizing replay");
    prompts.CommitSuccessfulRun();
    Check(!prompts.ObservedConfirmationStillVisible(promptBitmap), "completed route clears transient cancellation evidence");
    prompts = new LoginControlCache(confirmationDirectory, "account-A-profile-A");
    Check(prompts.MatchCharacterConfirmation(promptBitmap) is { CancelX: 493, CancelY: 258 }, "two verified reversible routes retain exact prompt pixels and validated cancel point");
    Check(prompts.MatchLogIn(promptBitmap) == null, "character signature cannot authorize a POL Log In click");
    Check(!new LoginControlCache(confirmationDirectory, "account-B-profile-A").HasReplay && !new LoginControlCache(confirmationDirectory, "account-A-profile-B").HasReplay, "character replay cannot cross account or profile bindings");
    promptPixels[54 + (214 * 640 + 290) * 4] ^= 1; File.WriteAllBytes(promptBitmap, promptPixels);
    Check(prompts.MatchCharacterConfirmation(promptBitmap) == null, "changed prompt pixels require fresh character discovery");
    Check(!File.ReadAllText(Directory.GetFiles(confirmationDirectory).Single()).Contains("Mainchar"), "character cache persists geometry and digest without recognized account text");
    File.WriteAllText(Directory.GetFiles(confirmationDirectory).Single(), "broken");
    Check(!new LoginControlCache(confirmationDirectory, "account-A-profile-A").HasReplay, "corrupt confirmation cache cannot authorize replay");
    controls.RecordSectionTrial(20, 8, true); controls.RecordSectionTrial(20, 8, true);
    Check(new LoginControlCache(controlsDirectory, "shared-POL-layout").ReplayPressMs == 80, "two section passes do not change production timing");
    controls.RecordSectionTrial(20, 8, true);
    controls = new LoginControlCache(controlsDirectory, "shared-POL-layout");
    Check(controls.ReplayPressMs == 20 && controls.ReplayPollMs == 8, "three accepted section replays persist production timing without executable updates");
    controls.RecordSectionTrial(1, 8, false);
    Check(new LoginControlCache(controlsDirectory, "shared-POL-layout").ReplayPressMs == 20, "rejected faster candidate preserves the previous validated timing");
    controls.RecordSectionTrial(20, 8, false);
    Check(new LoginControlCache(controlsDirectory, "shared-POL-layout").ReplayPressMs == 80, "rejected active timing restores the original input duration");
    controls.RecordSectionTrial(5, 8, true); controls.RecordSectionTrial(6, 8, true); controls.RecordSectionTrial(5, 8, true);
    Check(new LoginControlCache(controlsDirectory, "shared-POL-layout").ReplayPressMs == 80, "mixed candidates cannot combine their successful counts");
    var rejectedTiming = false;
    try { controls.RecordSectionTrial(0, 8, true); } catch (ArgumentOutOfRangeException) { rejectedTiming = true; }
    Check(rejectedTiming, "unbounded input timing is rejected before persistence");
    File.WriteAllText(Directory.GetFiles(controlsDirectory).Single(), "{broken");
    Check(!new LoginControlCache(controlsDirectory, "shared-POL-layout").HasReplay, "corrupt shared control cache falls back to discovery");
    Check(new LoginControlCache(controlsDirectory, "shared-POL-layout").ReplayPressMs == 80, "corrupt timing uses the original duration");
}
finally { Directory.Delete(directory, true); }
Console.WriteLine($"{passed} checks passed.");
var maintenanceStart = new DateTimeOffset(2026, 10, 10, 1, 0, 0, TimeSpan.Zero);
var polDialog = new LoginFrame(640, 480, [
    new("Connect to PlayOnline", []), new("Square Enix Password", []),
    new("Connect Settings Cancel", [new("Connect", 150, 280, 55, 16), new("Settings", 280, 280, 55, 16), new("Cancel", 430, 280, 45, 16)])]);
Check(LoginCheckpoints.PolDialogButton(polDialog, "Cancel")?.Words[0].X == 430, "POL Cancel resolves its actual word inside a combined button row");
Check(LoginCheckpoints.PolDialogButton(polDialog, "Connect")?.Words[0].X == 150, "POL Connect resolves its actual word inside a combined button row");
Check(LoginCheckpoints.PolDialogButton(polDialog with { Lines = polDialog.Lines.Append(new("Connect to PlayOnline", [new("Connect", 217, 170, 74, 12.5)])).ToArray() }, "Connect")?.Words[0].X == 150, "POL dialog title does not obscure the unique Connect button");
Check(LoginCheckpoints.PolDialogButton(polDialog with { Lines = polDialog.Lines.Append(new("Cancel", [new("Cancel", 431, 281, 45, 16)])).ToArray() }, "Cancel") != null, "overlapping crop readings preserve one physical POL Cancel button");
Check(LoginCheckpoints.PolDialogButton(polDialog with { Lines = polDialog.Lines.Append(new("Cancel", [new("Cancel", 500, 280, 40, 16)])).ToArray() }, "Cancel") == null, "physically separate POL Cancel candidates block input");
Check(LoginCheckpoints.PolDialogButton(polDialog with { Lines = polDialog.Lines.Append(new("Error code", [])).ToArray() }, "Cancel") == null, "error dialog cannot authorize a normal POL Cancel action");
Check(LoginCheckpoints.PolDialogButton(polDialog, "Yes") == null, "POL button fallback cannot target game confirmation");
var maintenanceEnd = maintenanceStart.AddHours(2);
var maintenanceWindow = new MaintenanceWindow("https://www.playonline.com/ff11us/polnews/news123.shtml", "Maintenance", maintenanceStart, maintenanceEnd);
var maintainedAccount = new AccountConfiguration(Guid.NewGuid(), "Main", "Main", "Hauplibond", "Odin", "", "", true);
Check(maintainedAccount.AutoLoginEnabled, "legacy maintained accounts include auto login");
Check(!RecoveryPolicy.Evaluate(maintainedAccount, false, [maintenanceWindow], maintenanceStart.AddMinutes(1)).Allowed, "maintenance prevents retries");
Check(!RecoveryPolicy.Evaluate(maintainedAccount, false, [maintenanceWindow], maintenanceEnd.AddMinutes(1)).Allowed, "post-maintenance grace period prevents mass retry");
Check(RecoveryPolicy.Evaluate(maintainedAccount, false, [maintenanceWindow], maintenanceEnd.AddMinutes(3)).Allowed, "reconnect eligible after maintenance");
Check(!RecoveryPolicy.Evaluate(maintainedAccount, true, [], maintenanceEnd).Allowed, "intentional shutdown prevents reconnect");
Check(RecoveryPolicy.Evaluate(maintainedAccount, false, [maintenanceWindow with { World = "Asura" }], maintenanceStart).Allowed, "unaffected world may reconnect");
var parsed = MaintenanceNoticeParser.Parse("https://www.playonline.com/ff11us/polnews/news123.shtml", "All Worlds Maintenance", "FINAL FANTASY XI [Date &amp; Time]<br>Sep. 28, 2026 22:00 to 23:30 (PDT)");
Check(parsed?.StartUtc == new DateTimeOffset(2026, 9, 29, 5, 0, 0, TimeSpan.Zero), "official PDT time converted to UTC");
Check(parsed?.EndUtc == new DateTimeOffset(2026, 9, 29, 6, 30, 0, TimeSpan.Zero), "official maintenance end parsed");
Check(MaintenanceNoticeParser.Parse("https://www.playonline.com/ff11us/polnews/news123.shtml", "World Transfer Maintenance", "FINAL FANTASY XI [Date & Time] Sep. 28, 2026 22:00 to 23:30 (PDT)") == null, "transfer-only maintenance does not pause game");
Check(MaintenanceNoticeParser.Parse("https://www.playonline.com/ff11us/polnews/news123.shtml", "All Worlds Maintenance", "FINAL FANTASY XI [Date & Time] Tomorrow until sometime") == null, "unknown notice times are not guessed");
var ipcDirectory = Path.Combine(Path.GetTempPath(), "ffxi-ipc-" + Guid.NewGuid());
Directory.CreateDirectory(ipcDirectory);
try
{
    var now = DateTimeOffset.UtcNow;
    var sessionNonce = "1234567890_abcdef0123456789";
    var stem = Path.Combine(ipcDirectory, "odin_hauplibond");
    File.WriteAllText(stem + ".status", $"{sessionNonce}\nHauplibond\nOdin\n{now.AddMinutes(-1).ToUnixTimeSeconds()}\n{now.ToUnixTimeSeconds()}\n");
    File.WriteAllText(Path.Combine(ipcDirectory, "control.token"), new string('a', 64));
    var ipc = new ShutdownIpc(ipcDirectory);
    var observedSession = new GameSession("0123456789abcdef", "Mainchar", "Asura", DateTimeOffset.UtcNow.AddSeconds(-5), DateTimeOffset.UtcNow.AddSeconds(-1));
    var advancedSession = observedSession with { HeartbeatUtc = observedSession.HeartbeatUtc.AddSeconds(1) };
    Check(ShutdownIpc.HasAdvanced(observedSession, advancedSession), "online verification accepts the same session's next heartbeat without a fixed wait");
    Check(!ShutdownIpc.HasAdvanced(observedSession, observedSession), "an unchanged heartbeat cannot certify online verification");
    Check(!ShutdownIpc.HasAdvanced(observedSession, observedSession with { HeartbeatUtc = observedSession.HeartbeatUtc.AddSeconds(-1) }), "a backwards heartbeat cannot certify online verification");
    Check(!ShutdownIpc.HasAdvanced(observedSession, advancedSession with { Nonce = "abcdef0123456789" }), "a replacement session must establish its own advancing heartbeat");
    Check(!ShutdownIpc.HasAdvanced(observedSession, advancedSession with { RegisteredUtc = observedSession.RegisteredUtc.AddSeconds(1) }), "changed registration cannot inherit heartbeat proof");
    Check(!ShutdownIpc.HasAdvanced(observedSession, advancedSession with { Character = "Otherchar" }), "another character cannot inherit heartbeat proof");
    Check(!ShutdownIpc.HasAdvanced(observedSession, advancedSession with { World = "Odin" }), "another world cannot inherit heartbeat proof");
    var game = ipc.ReadSession(maintainedAccount, now)!;
    Check(game != null, "fresh matching character heartbeat accepted");
    Check(ipc.DiscoverCharacter("Hauplibond", now.AddMinutes(-2), now)?.World == "Odin", "current world collected from matching fresh heartbeat");
    Check(ipc.DiscoverCharacter("Hauplibond", now.AddSeconds(5), now) == null, "older process heartbeat cannot set current world");
    Check(ipc.ReadSession(maintainedAccount with { World = "Asura" }, now) == null, "shutdown cannot target another world");
    Check(ipc.ReadSession(maintainedAccount, now.AddMinutes(1)) == null, "stale heartbeat cannot receive shutdown");
    var stateReader = new CharacterStateReader(ipcDirectory);
    Check(stateReader.Read(maintainedAccount, now) == null, "legacy addon has no player snapshot");
    var statePayload = $"{sessionNonce}\nHauplibond\nOdin\n{now.AddMinutes(-1).ToUnixTimeSeconds()}\n{now.ToUnixTimeSeconds()}\n0\n1500\n200\n500\n1\n100\nWest Ronfaure\n";
    File.WriteAllText(stem + ".state", statePayload + "0\n");
    var state = stateReader.Read(maintainedAccount, now)!;
    Check(state.IsDead && state.Hp == 0 && state.Mp == 200 && state.Zone == "West Ronfaure", "player stats death and zone parsed");
    Check(state.Describe(true).Contains("Current · West Ronfaure · Dead"), "live death displayed");
    var jobsPayload = $"{sessionNonce}\nHauplibond\nOdin\n{now.AddMinutes(-1).ToUnixTimeSeconds()}\n{now.ToUnixTimeSeconds()}\n46\nDRK\n99\nWAR\n57\n";
    File.WriteAllText(stem + ".jobs", jobsPayload);
    Check(stateReader.Read(maintainedAccount, now)!.Describe(true).StartsWith("ML 46 99DRK/57WAR\n"), "actual job levels and master level use the requested compact format");
    foreach (var invalidJobs in new[] { jobsPayload.Replace(sessionNonce, "different_session"), jobsPayload.Replace("Hauplibond", "Otherchar"), jobsPayload.Replace("Odin", "Asura"), jobsPayload.Replace(now.ToUnixTimeSeconds().ToString(), now.AddSeconds(-1).ToUnixTimeSeconds().ToString()), jobsPayload.Replace("\n46\n", "\n51\n"), jobsPayload.Replace("DRK", "XYZ"), jobsPayload.Replace("\n99\n", "\n100\n"), jobsPayload.Replace("\n57\n", "\n0\n"), jobsPayload + "extra\n" })
    {
        File.WriteAllText(stem + ".jobs", invalidJobs);
        Check(stateReader.Read(maintainedAccount, now) is { Jobs: null, MaxHp: 1500 }, "invalid or mismatched jobs do not replace valid vitals");
    }
    File.WriteAllText(stem + ".jobs", jobsPayload.Replace("\n46\n", "\n-1\n"));
    Check(stateReader.Read(maintainedAccount, now)!.Jobs!.Describe() == "ML ? 99DRK/57WAR", "unknown master level is not fabricated");
    File.WriteAllText(stem + ".jobs", jobsPayload.Replace("\n99\n", "\n50\n").Replace("WAR\n57", "NON\n0"));
    Check(stateReader.Read(maintainedAccount, now)!.Jobs!.Describe() == "50DRK", "lower level character without subjob omits master and subjob");
    File.WriteAllText(stem + ".jobs", jobsPayload);
    var environmentPayload = $"{sessionNonce}\nHauplibond\nOdin\n{now.AddMinutes(-1).ToUnixTimeSeconds()}\n{now.ToUnixTimeSeconds()}\n100\nRain\n1184\nFiresday\nWaxing Crescent\n25\n";
    File.WriteAllText(stem + ".environment", environmentPayload);
    Check(stateReader.Read(maintainedAccount, now)!.Describe(true).Contains("Weather: Rain · Vana’diel: 19:44 Firesday · Moon: Waxing Crescent (25%)"), "live zone weather game time and moon shown from matching report");
    foreach (var invalidEnvironment in new[] { environmentPayload.Replace(sessionNonce, "different_session"), environmentPayload.Replace("\n100\n", "\n101\n"), environmentPayload.Replace("\n1184\n", "\n1440\n"), environmentPayload.Replace("\n25\n", "\n101\n"), environmentPayload.Replace(now.ToUnixTimeSeconds().ToString(), now.AddSeconds(-1).ToUnixTimeSeconds().ToString()) })
    {
        File.WriteAllText(stem + ".environment", invalidEnvironment);
        Check(stateReader.Read(maintainedAccount, now) is { Environment: null }, "mismatched or invalid environment never replaces valid vitals");
    }
    File.WriteAllText(stem + ".environment", environmentPayload);
    Check(stateReader.Read(maintainedAccount with { Character = "Other" }, now) == null, "player stats bound to identity");
    File.WriteAllText(stem + ".state", statePayload + now.ToUnixTimeSeconds() + "\n");
    var logoutState = new CharacterStateReader(ipcDirectory).Read(maintainedAccount, now.AddDays(1))!;
    Check(logoutState.IsDead && logoutState.LoggedOutUtc != null && logoutState.Describe(false).Contains("Dead at logout"), "dead logout stats survive restart and stale heartbeat");
    File.WriteAllText(stem + ".state", statePayload + now.AddMinutes(1).ToUnixTimeSeconds() + "\n");
    Check(stateReader.Read(maintainedAccount, now) == null, "future logout timestamp rejected");
    File.WriteAllText(stem + ".state", statePayload.Replace("\n200\n500\n", "\n-1\n500\n") + "0\n");
    Check(stateReader.Read(maintainedAccount, now) == null, "invalid stats rejected");
    File.WriteAllText(stem + ".state", statePayload + "0\nextra\n");
    Check(stateReader.Read(maintainedAccount, now) == null, "malformed snapshot rejected");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    var receiver = Task.Run(async () =>
    {
        while (!File.Exists(stem + ".request")) await Task.Delay(10, timeout.Token);
        var request = File.ReadAllLines(stem + ".request");
        Check(request[2] == sessionNonce && request[3] == "shutdown", "shutdown request bound to exact session");
        File.Delete(stem + ".request");
        File.WriteAllText(stem + ".receipt", $"{request[1]}\n{sessionNonce}\ndispatched\n");
    });
    await ipc.RequestAsync(maintainedAccount, game!, timeout.Token);
    await receiver;
    Check(!File.Exists(stem + ".request"), "acknowledged shutdown consumed without process killing");
    var cancellationReceiver = Task.Run(async () =>
    {
        while (!File.Exists(stem + ".request")) await Task.Delay(10, timeout.Token);
        var request = File.ReadAllLines(stem + ".request");
        Check(request[2] == sessionNonce && request[3] == "cancel_shutdown", "countdown cancellation targets the same authenticated game session");
        File.Delete(stem + ".request");
        File.WriteAllText(stem + ".receipt", $"{request[1]}\n{sessionNonce}\ndispatched\n");
    });
    await ipc.CancelAsync(maintainedAccount, game!, timeout.Token);
    await cancellationReceiver;
}
finally { Directory.Delete(ipcDirectory, true); }
Console.WriteLine($"{passed} total checks passed.");
var serviceA = maintainedAccount with { PlayOnlineId = "ABCD1234", SquareEnixId = "SharedSE" };
var serviceB = maintainedAccount with { Id = Guid.NewGuid(), Character = "Other", PlayOnlineId = "EFGH5678", SquareEnixId = "SharedSE" };
Check(!AccountIdentity.Conflicts(serviceA, serviceB), "multiple POL accounts may share a Square Enix ID");
Check(AccountIdentity.Conflicts(serviceA, serviceB with { PlayOnlineId = "ABCD1234" }), "duplicate POL service account rejected");
Check(AccountIdentity.Conflicts(serviceA, serviceB with { Character = serviceA.Character }), "duplicate character on same world rejected");
Console.WriteLine($"{passed} total checks passed.");

var scheduled = maintainedAccount with { ScheduleMaintenance = true };
var fresh = new MaintenanceSnapshot([maintenanceWindow], maintenanceStart.AddMinutes(-4));
Check(MaintenanceSchedule.ShutdownDue(scheduled, fresh, maintenanceStart.AddMinutes(-4)) == maintenanceWindow, "scheduled shutdown occurs five minutes before maintenance");
Check(MaintenanceSchedule.ShutdownDue(scheduled, fresh, maintenanceStart.AddMinutes(-6)) == null, "upcoming maintenance does not shut down too early");
Check(MaintenanceSchedule.ShutdownDue(maintainedAccount, fresh, maintenanceStart) == null, "maintenance shutdown is explicitly enabled");
Check(MaintenanceSchedule.ShutdownDue(scheduled with { World = "Asura" }, fresh with { Windows = [maintenanceWindow with { World = "Odin" }] }, maintenanceStart) == null, "world-specific shutdown leaves other worlds running");
Check(MaintenanceSchedule.ShutdownDue(scheduled with { World = "" }, fresh, maintenanceStart) == null, "unknown world cannot schedule automatic shutdown");
Check(MaintenanceSchedule.ShutdownDue(scheduled, fresh with { CheckedUtc = maintenanceStart.AddHours(-1) }, maintenanceStart) == null, "stale notices cannot trigger shutdown");
Check(!RecoveryPolicy.Evaluate(scheduled, false, fresh, maintenanceStart.AddMinutes(-4)).Allowed, "scheduled downtime prevents premature reconnect before start");
Check(!RecoveryPolicy.Evaluate(scheduled, false, fresh with { ReviewRequired = ["Unparsed notice"] }, maintenanceEnd.AddMinutes(3)).Allowed, "unparsed maintenance notice requires review before reconnect");
Console.WriteLine($"{passed} final checks passed.");

var legacyCredentials = System.Text.Json.JsonSerializer.Deserialize<AccountCredentials>("{\"PolPassword\":\"synthetic-pol\",\"SquareEnixPassword\":\"synthetic-se\"}");
Check(legacyCredentials?.MemberPassword == "", "existing encrypted credential shape remains compatible with optional member password");
var legacyAccount = System.Text.Json.JsonSerializer.Deserialize<AccountConfiguration>("{\"Id\":\"00000000-0000-0000-0000-000000000001\",\"Label\":\"Main\",\"Profile\":\"Main\",\"Character\":\"Mainchar\",\"World\":\"Asura\",\"PolMember\":\"Member\",\"SquareEnixId\":\"SE\"}");
Check(legacyAccount?.PolPasswordStoredInPol == true && !legacyAccount.MemberPasswordRequired && !legacyAccount.UsesOneTimePassword, "existing accounts default to saved POL password without optional prompts");
Console.WriteLine($"{passed} final checks passed.");

var easternDisplayZone = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Eastern Standard Time" : "America/New_York");
var displayStart = new DateTimeOffset(2026, 10, 13, 5, 0, 0, TimeSpan.Zero);
var displayRange = MaintenanceTimeDisplay.Range(displayStart, displayStart.AddMinutes(90), easternDisplayZone);
Check(displayRange.Contains(easternDisplayZone.DaylightName) && displayRange.Contains("UTC-04:00"), "maintenance display names the applicable local daylight time zone and UTC offset");
Check(TimeZoneInfo.ConvertTime(displayStart, easternDisplayZone).Day == 13 && TimeZoneInfo.ConvertTime(displayStart, easternDisplayZone).Hour == 1, "Pacific maintenance crossing midnight displays the correct local calendar day and hour");
var pacificRange = MaintenanceTimeDisplay.PacificRange(displayStart, displayStart.AddMinutes(90));
Check(pacificRange.Contains("Oct 12, 2026 10:00 PM") && pacificRange.Contains("11:30 PM") && pacificRange.Contains("UTC-07:00"), "Pacific reference range explains the previous-day notice date alongside local time");
var fallbackRange = MaintenanceTimeDisplay.Range(new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero), easternDisplayZone);
Check(fallbackRange.Contains(easternDisplayZone.DaylightName) && fallbackRange.Contains(easternDisplayZone.StandardName) && fallbackRange.Contains("UTC-04:00") && fallbackRange.Contains("UTC-05:00"), "a maintenance window crossing daylight saving fallback labels both offsets");
Check(MaintenanceTimeDisplay.Instant(displayStart, TimeZoneInfo.Utc).Contains("UTC+00:00"), "maintenance resume times explicitly label UTC systems too");
var scheduleNow = new DateTimeOffset(2026, 10, 10, 1, 0, 0, TimeSpan.Zero);
var globalNext = maintenanceWindow with { StartUtc = scheduleNow.AddHours(2), EndUtc = scheduleNow.AddHours(3), Title = "Global upcoming" };
var odinNext = globalNext with { World = "Odin", StartUtc = scheduleNow.AddHours(1), EndUtc = scheduleNow.AddHours(2), Title = "Odin upcoming" };
var asuraNext = odinNext with { World = "Asura", StartUtc = scheduleNow.AddMinutes(20), Title = "Asura upcoming" };
var expiredNotice = odinNext with { StartUtc = scheduleNow.AddHours(-2), EndUtc = scheduleNow.AddHours(-1) };
Check(MaintenanceSchedule.UpcomingForCharacter([globalNext, odinNext, asuraNext, expiredNotice], "Odin", scheduleNow).SequenceEqual([odinNext, globalNext]), "per-character maintenance combines global and matching world, sorted by next occurrence");
Check(MaintenanceSchedule.UpcomingForCharacter([globalNext, odinNext, asuraNext], "", scheduleNow).SequenceEqual([globalNext]), "unknown world shows global maintenance without inferring a world match");
Check(MaintenanceSchedule.UpcomingForCharacter([globalNext, odinNext, asuraNext], "ASURA", scheduleNow).SequenceEqual([asuraNext, globalNext]), "world maintenance matching is case insensitive");
var ongoing = odinNext with { StartUtc = scheduleNow.AddMinutes(-10), EndUtc = scheduleNow.AddMinutes(10) };
Check(MaintenanceSchedule.UpcomingForCharacter([globalNext, ongoing], "Odin", scheduleNow).First() == ongoing, "in-progress maintenance remains visible before the next upcoming window");
Console.WriteLine($"{passed} final checks passed.");

var scopedLegacy = MaintenanceNoticeParser.Parse("https://www.playonline.com/ff11us/polnews/news12477.shtml", "Odin World Emergency Maintenance", "FINAL FANTASY XI [Date & Time] Jan. 29, 2008 from 9:00 to 11:00 (PST) [Affected World] Odin World", "Odin");
Check(scopedLegacy?.World == "Odin" && scopedLegacy.StartUtc == new DateTimeOffset(2008, 1, 29, 17, 0, 0, TimeSpan.Zero), "world-specific notice accepts explicit from wording and PST");
Check(MaintenanceNoticeParser.Parse("https://www.playonline.com/ff11us/polnews/news123.shtml", "All Worlds Maintenance", "FINAL FANTASY XI [Date & Time] Oct. 10, 2026 1:00 to 2:00 (UTC) Oct. 11, 2026 1:00 to 2:00 (UTC)") == null, "multiple maintenance ranges require review instead of silently using the first");
Console.WriteLine($"{passed} final checks passed.");

var initBytes = System.Text.Encoding.UTF8.GetBytes("wait 5\r\nlua load OtherAddon\r\n");
var initUpdate = WindowerStartupScript.EnsureAutoLoad(initBytes);
Check(initUpdate.Changed && initUpdate.Content.AsSpan().EndsWith(initBytes), "startup addition preserves every existing byte and command");
Check(!WindowerStartupScript.EnsureAutoLoad(initUpdate.Content).Changed, "startup deployment is idempotent");
Check(WindowerStartupScript.EnsureAutoLoad(System.Text.Encoding.ASCII.GetBytes("//lua load FFXIManager\nlua load FFXIManagerOther\n")).Changed, "commented or different addon does not count as loaded");
var utf16Init = System.Text.Encoding.Unicode.GetPreamble().Concat(System.Text.Encoding.Unicode.GetBytes("wait 5\n")).ToArray();
var utf16Updated = WindowerStartupScript.EnsureAutoLoad(utf16Init).Content;
Check(utf16Updated.AsSpan().StartsWith(System.Text.Encoding.Unicode.GetPreamble()) && System.Text.Encoding.Unicode.GetString(utf16Updated).Contains("lua load FFXIManager\nwait 5\n"), "startup preserves UTF16 BOM and line endings");
Check(!WindowerStartupScript.EnsureAutoLoad(System.Text.Encoding.ASCII.GetBytes("lua l \"FFXIManager\"; echo hi\n")).Changed, "quoted load alias does not duplicate startup entry");
var alertTime = scheduleNow;
var alert = InGameMaintenance.Parse(["1234567890abcdef_123456", "Mainchar", "Odin", alertTime.AddHours(-1).ToUnixTimeSeconds().ToString(), alertTime.ToUnixTimeSeconds().ToString(), "incoming-017-system", "Emergency maintenance will begin shortly."], alertTime) ?? throw new Exception("Valid alert rejected");
Check(InGameMaintenance.Hold([alert], new([], alertTime), alertTime)?.Allowed == false, "in-game warning without end time holds reconnect for review");
Check(InGameMaintenance.Hold([alert], new([odinNext], alertTime), alertTime)?.ResumeUtc == odinNext.EndUtc.AddMinutes(2), "in-game warning uses relevant official maintenance end plus buffer");
Check(InGameMaintenance.Hold([alert], new([odinNext], odinNext.EndUtc.AddMinutes(3)), odinNext.EndUtc.AddMinutes(3)) == null, "confirmed completed maintenance releases in-game hold");
Check(InGameMaintenance.Hold([alert], new([asuraNext], alertTime), alertTime)?.ResumeUtc == null, "another world's maintenance cannot resolve warning");
Check(InGameMaintenance.Parse(["1234567890abcdef_123456", "Mainchar", "Odin", "1", "999999999999999999", "incoming-017-system", "maintenance"], alertTime) == null, "invalid in-game timestamps are rejected");
Console.WriteLine($"{passed} final checks passed.");
var alertsDirectory = Path.Combine(Path.GetTempPath(), "ffxi-alerts-" + Guid.NewGuid());
Directory.CreateDirectory(alertsDirectory);
try
{
    var alertPath = Path.Combine(alertsDirectory, "odin_mainchar.alert");
    void WriteAlert(InGameMaintenanceAlert value) => File.WriteAllLines(alertPath, [value.Session, value.Character, value.World, value.RegisteredUtc.ToUnixTimeSeconds().ToString(), value.ObservedUtc.ToUnixTimeSeconds().ToString(), "incoming-017-system", value.Message]);
    WriteAlert(alert);
    Check(InGameMaintenance.Read(alertsDirectory, "Odin", alertTime).Count == 1 && InGameMaintenance.Read(alertsDirectory, "Asura", alertTime).Count == 0, "persisted warning is scoped to its observed world");
    InGameMaintenance.Acknowledge(alertsDirectory, alert);
    Check(InGameMaintenance.Read(alertsDirectory, "Odin", alertTime).Count == 0, "resolved warning stays acknowledged after rereading disk");
    WriteAlert(alert with { ObservedUtc = alertTime.AddSeconds(1) });
    Check(InGameMaintenance.Read(alertsDirectory, "Odin", alertTime.AddSeconds(1)).Count == 1, "acknowledgment never clears a newer concurrent warning");
}
finally { Directory.Delete(alertsDirectory, true); }
Console.WriteLine($"{passed} final checks passed.");
Check(!ShutdownCleanupPolicy.CanTerminate(false, TimeSpan.FromMinutes(10), false), "unacknowledged shutdown cannot automatically force terminate");
Check(!ShutdownCleanupPolicy.CanTerminate(true, TimeSpan.FromSeconds(89), false), "graceful shutdown has full timeout before forced cleanup");
Check(!ShutdownCleanupPolicy.CanTerminate(true, TimeSpan.FromSeconds(90), true), "fresh game heartbeat blocks automatic forced cleanup");
Check(ShutdownCleanupPolicy.CanTerminate(true, TimeSpan.FromSeconds(90), false), "confirmed timed-out shutdown without heartbeat permits cleanup");
Check(ShutdownCleanupPolicy.CanTerminate(true, TimeSpan.FromSeconds(15), false, TimeSpan.FromSeconds(5)), "confirmed logout allows leftover process cleanup after five seconds without waiting ninety");
Check(!ShutdownCleanupPolicy.CanTerminate(true, TimeSpan.FromSeconds(15), false, TimeSpan.FromSeconds(4.9)), "confirmed logout receives its full five-second process-exit grace period");
Check(!ShutdownCleanupPolicy.CanTerminate(true, TimeSpan.FromSeconds(15), true, TimeSpan.FromSeconds(5)), "a live character blocks fast cleanup even with an earlier logout observation");
Check(!ShutdownCleanupPolicy.CanTerminate(false, TimeSpan.FromSeconds(15), false, TimeSpan.FromSeconds(5)), "fast cleanup also requires acknowledged dispatch");
Check(ShutdownCleanupPolicy.ShouldRetry(true, TimeSpan.FromSeconds(45), 0), "live character receives a graceful retry after the countdown allowance");
Check(!ShutdownCleanupPolicy.ShouldRetry(true, TimeSpan.FromSeconds(44), 0), "an ongoing countdown is not restarted early");
Check(!ShutdownCleanupPolicy.ShouldRetry(false, TimeSpan.FromSeconds(45), 0), "disconnected characters do not receive countdown retries");
Check(!ShutdownCleanupPolicy.ShouldRetry(true, TimeSpan.FromSeconds(45), 2), "repeated shutdown cancellations stop after bounded retries");
Console.WriteLine($"{passed} final checks passed.");
LoginFrame LoginScreen(params string[] lines) => new(640, 480, lines.Select(t => new OcrLine(t, t.Split(' ').Select((w, i) => new OcrWord(w, i * 10, 20, 10, 10)).ToArray())).ToArray());
var staticPolMenu = new LoginFrame(640, 480, [new("Login Information", []), new("Member Information", []), new("Log In'", [new("Log", 102, 153.5, 28, 15.5), new("In'", 140, 153.5, 28.5, 13.5)]), new("Login", [new("Login", 326, 150.7, 42, 13.3)])]);
Check(staticPolMenu.UniqueLine("Log In") == null && LoginCheckpoints.PolMenuButton(staticPolMenu, "Log In")?.Words[0].X == 102, "cropped Login Information heading cannot compete with the real left-menu Log In button");
Check(LoginCheckpoints.PolMenuButton(staticPolMenu with { Lines = [..staticPolMenu.Lines, new("Log In", [new("Log In", 101, 154, 68, 15)])] }, "Log In") != null, "overlapping left-menu reads still resolve one physical button");
Check(LoginCheckpoints.PolMenuButton(staticPolMenu with { Lines = [..staticPolMenu.Lines, new("Log In", [new("Log In", 101, 220, 68, 15)])] }, "Log In") == null, "two separate menu candidates remain blocked");
LoginFrame HighlightedLogIn(double scale) => new((int)(640 * scale), (int)(480 * scale), [new("Login Information", []), new("Member Information", []),
    new("Settings", [new("Settings", 83.5 * scale, 184.5 * scale, 70 * scale, 15.5 * scale)]),
    new("Delete", [new("Delete", 95.5 * scale, 216.5 * scale, 58 * scale, 13.5 * scale)]),
    new("Back", [new("Back", 113 * scale, 249 * scale, 41 * scale, 13 * scale)])]);
foreach (var scale in new[] { 1d, 2d, 3d, 4d })
{
    var point = LoginCheckpoints.PolLogInPoint(HighlightedLogIn(scale));
    Check(point != null && point.Value.X / scale is > 100 and < 154 && point.Value.Y / scale is > 149 and < 170,
        "highlighted Log In missing from OCR uses the independently anchored scalable menu route");
}
Check(LoginCheckpoints.PolLogInPoint(HighlightedLogIn(1) with { Lines = HighlightedLogIn(1).Lines.Where(l => l.Text != "Back").ToArray() }) == null,
    "incomplete menu anchors cannot authorize a fallback click");
Check(LoginCheckpoints.PolLogInPoint(HighlightedLogIn(1) with { Lines = [..HighlightedLogIn(1).Lines, new("Connect to PlayOnline", []), new("Square Enix Password", [])] }) == null,
    "Connect dialog forbids Log In fallback even with menu visible behind it");
{
    var anchoredDirectory = Path.Combine(Path.GetTempPath(), "ffxi-anchored-menu-" + Guid.NewGuid());
    Directory.CreateDirectory(anchoredDirectory);
    var bitmapPath = Path.Combine(anchoredDirectory, "control.bmp");
    var bitmap = new byte[54 + 640 * 480 * 4];
    BitConverter.GetBytes((ushort)0x4d42).CopyTo(bitmap, 0); BitConverter.GetBytes(54).CopyTo(bitmap, 10);
    BitConverter.GetBytes(40).CopyTo(bitmap, 14); BitConverter.GetBytes(640).CopyTo(bitmap, 18);
    BitConverter.GetBytes(-480).CopyTo(bitmap, 22); BitConverter.GetBytes((ushort)1).CopyTo(bitmap, 26);
    BitConverter.GetBytes((ushort)32).CopyTo(bitmap, 28); File.WriteAllBytes(bitmapPath, bitmap);
    var cache = new LoginControlCache(anchoredDirectory, "highlighted-menu");
    cache.ObserveLogIn(HighlightedLogIn(1), bitmapPath);
    Check(!new LoginControlCache(anchoredDirectory, "highlighted-menu").HasReplay, "unverified highlighted control cannot activate trained replay");
    cache.CommitSuccessfulRun();
    cache = new LoginControlCache(anchoredDirectory, "highlighted-menu");
    Check(!cache.HasReplay, "one successful highlighted-button observation keeps the established two-success promotion rule");
    cache.ObserveLogIn(HighlightedLogIn(1), bitmapPath); cache.CommitSuccessfulRun();
    cache = new LoginControlCache(anchoredDirectory, "highlighted-menu");
    Check(cache.MatchLogIn(bitmapPath) != null, "highlighted Log In can join the shared trained cache after two successful routes");
    bitmap[54 + (160 * 640 + 128) * 4] = 1; File.WriteAllBytes(bitmapPath, bitmap);
    Check(cache.MatchLogIn(bitmapPath) == null, "changed highlighted-button pixels cannot replay the trained click");
    Directory.Delete(anchoredDirectory, true);
}
Check(!LoginCheckpoints.IsCredentialRejection(LoginScreen("Member Information", "Square Enix Password")) && !LoginCheckpoints.IsCredentialRejection(LoginScreen("Error code", "Connection failed")), "OCR misses and connection errors do not produce false credential alerts");
Check(LoginCheckpoints.IsCredentialRejection(LoginScreen("The Square Enix ID or password is incorrect")), "explicit POL password rejection is classified for the requested warning");
Check(LoginCheckpoints.IsCredentialRejection(LoginScreen("Square Enix Account Certification Error", "Error code:POL-5311", "The Square Enix ID, Square Enix password, or", "one-time password entered is incorrect.")), "actual POL-5311 screenshot wording reports credential rejection");
Check(LoginCheckpoints.Recognize(LoginScreen("Member List", "Add Member")) == LoginCheckpoint.MemberList, "POL saved members require both list markers");
Check(LoginCheckpoints.Recognize(LoginScreen("Member List")) == LoginCheckpoint.Unknown, "one stray label cannot authorize a member click");
Check(LoginCheckpoints.Recognize(LoginScreen("Member Information", "Login Information", "Log In", "Error code")) == LoginCheckpoint.Error, "POL errors override actionable member screens");
Check(LoginCheckpoints.Recognize(LoginScreen("Connect to PlayOnline", "Square Enix Password")) == LoginCheckpoint.Connect, "SE password dialog recognized independently from software keyboard");
Check(LoginCheckpoints.Recognize(LoginScreen("Please enter your password", "software keyboard")) == LoginCheckpoint.PasswordEditor, "password editor requires its keyboard warning");
LoginFrame PasswordDialog(int width, int height) => new(width, height,
[
    new("Connect to PlayOnline", [new("Connect", width * .34, height * .35, width * .1, height * .03)]),
    new("Square Enix Password", [new("Square Enix Password", width * .18, height * .415, width * .3, height * .035)])
]);
var smallField = LoginCheckpoints.PasswordField(PasswordDialog(640, 480));
var largeField = LoginCheckpoints.PasswordField(PasswordDialog(1280, 960));
Check(smallField != null && largeField != null && largeField.Value.X == smallField.Value.X * 2 && largeField.Value.Y == smallField.Value.Y * 2, "POL password field follows detected label row and scales with client dimensions");
foreach (var width in new[] { 800, 1600, 3200, 5120 })
{
    var height = width * 3 / 4;
    var point = LoginCheckpoints.PasswordField(PasswordDialog(width, height));
    Check(point != null && Math.Abs(point.Value.X / width - .705) < .000001 && Math.Abs(point.Value.Y / height - .4325) < .000001, "password input stays relative to client bounds across varying sizes");
}
var passwordDialog = PasswordDialog(640, 480);
Check(LoginCheckpoints.PasswordField(passwordDialog with { Lines = [..passwordDialog.Lines, passwordDialog.Lines[1]] }) == null, "duplicate password labels block field input");
Check(LoginCheckpoints.PasswordField(passwordDialog with { Lines = [passwordDialog.Lines[1]] }) == null, "password label alone cannot authorize a field click");
Check(LoginCheckpoints.PasswordField(passwordDialog with { Lines = [passwordDialog.Lines[0], new("Square Enix Password", [new("Square Enix Password", 115, 20, 190, 18)])] }) == null, "password label outside the known dialog blocks a field click");
Check(LoginCheckpoints.Recognize(LoginScreen("Navigator", "Log Out", "FINAL FANTASY XI", "Maintenance news")) == LoginCheckpoint.Home, "maintenance headline on POL home is not a connection error");
Check(LoginCheckpoints.Recognize(LoginScreen("Select Character", "Create Character", "Delete Character")) == LoginCheckpoint.Title, "FFXI title does not imply a selected character");
var earlyTitle = new LoginFrame(1280, 800, [new("Select Character", [new("Select Character", 500, 400, 180, 20)])]);
Check(LoginCheckpoints.CanOpenCharacterList(earlyTitle), "early title control opens list without waiting for other labels or rendering");
Check(!LoginCheckpoints.CanOpenCharacterList(earlyTitle with { Lines = [..earlyTitle.Lines, earlyTitle.Lines[0]] }), "duplicate title control blocks early Enter");
Check(!LoginCheckpoints.CanOpenCharacterList(earlyTitle with { Lines = [..earlyTitle.Lines, new("Log in with Mainchar?", [])] }), "unread final confirmation blocks title Enter");
Check(!LoginCheckpoints.CanOpenCharacterList(earlyTitle with { Lines = [..earlyTitle.Lines, new("Connection failed", [])] }), "game error blocks early title Enter");
Check(!LoginCheckpoints.CanOpenCharacterList(earlyTitle with { Lines = [new("Select Character", [new("Select Character", double.NaN, 400, 180, 20)])] }), "invalid title geometry blocks early Enter");
Check(!LoginCheckpoints.CanOpenCharacterList(earlyTitle with { Width = 600 }), "out-of-client title control blocks early Enter");
Check(!LoginCheckpoints.CanOpenCharacterList(earlyTitle with { Lines = [..earlyTitle.Lines, new("Select a character to play", [])] }), "character list cannot be treated as title during transition");
Check(LoginCheckpoints.Recognize(LoginScreen("Select a character to play", "0 more characters can be made")) == LoginCheckpoint.Characters, "character selection can use the independent remaining-character counter");
Check(LoginCheckpoints.Recognize(LoginScreen("Select a character to play")) == LoginCheckpoint.Unknown, "character footer alone cannot authorize selection input");
var partialCharacters = new LoginFrame(1280, 800, [new("Select a character to play", [new("Select", 500, 770, 40, 14)])]);
Check(LoginCheckpoints.CanOpenDefaultCharacterConfirmation(partialCharacters), "partial rendered footer can open default-character dialog without counter discovery");
Check(!LoginCheckpoints.CanOpenDefaultCharacterConfirmation(partialCharacters with { Lines = [..partialCharacters.Lines, new("Log in with \"Nanakia\"?", [])] }), "existing unread confirmation cannot receive another opening Enter");
Check(!LoginCheckpoints.CanOpenDefaultCharacterConfirmation(partialCharacters with { Lines = [..partialCharacters.Lines, partialCharacters.Lines[0]] }), "ambiguous footer cannot open character dialog");
Check(!LoginCheckpoints.CanOpenDefaultCharacterConfirmation(partialCharacters with { Height = 1600 }), "footer must occupy rendered bottom UI region");
Check(!LoginCheckpoints.CanOpenDefaultCharacterConfirmation(partialCharacters with { Lines = [..partialCharacters.Lines, new("Connection failed", [])] }), "error screen cannot authorize early character opening");
var namedGrid = partialCharacters with { Lines = [..partialCharacters.Lines, new("Nanakia", [new("Nanakia", 940, 560, 80, 14)]), new("Nanikii", [new("Nanikii", 940, 580, 80, 14)])] };
Check(LoginCheckpoints.ConfiguredCharacterRow(namedGrid, "Nanakia") == (980d, 567d), "exact configured grid row excludes similar names");
Check(LoginCheckpoints.ConfiguredCharacterRow(namedGrid, "Nanaki") == null, "partial character name cannot authorize row selection");
Check(LoginCheckpoints.ConfiguredCharacterRow(namedGrid with { Lines = [..namedGrid.Lines, new("Nanakia", [new("Nanakia", 1140, 560, 80, 14)])] }, "Nanakia") == null, "separate duplicate character rows block selection");
Check(LoginCheckpoints.ConfiguredCharacterRow(namedGrid with { Lines = [..namedGrid.Lines, new("Error code", [])] }, "Nanakia") == null, "error overlay blocks configured row selection");
Check(LoginCheckpoints.ConfiguredCharacterRow(namedGrid with { Lines = [..namedGrid.Lines, new("Log in with \"Nanakia\"?", [])] }, "Nanakia") == null, "open confirmation blocks grid click");
LoginFrame CharacterGrid(int width, int height, double scale) => new(width, height,
[
    new("Select a character to play", [new("Select", 615 * scale, 834 * scale, 52 * scale, 14 * scale)]),
    new("0 more characters can be made", [new("characters", width - 150, 60, 75, 14)])
]);
var grid = CharacterGrid(1440, 900, 1);
var firstCandidate = LoginCheckpoints.InitialCharacterCandidate(grid);
var scaledCandidate = LoginCheckpoints.InitialCharacterCandidate(CharacterGrid(2880, 1800, 2));
Check(firstCandidate != null && scaledCandidate != null && scaledCandidate.Value.X == firstCandidate.Value.X * 2 && scaledCandidate.Value.Y == firstCandidate.Value.Y * 2, "character grid follows the measured UI font and footer anchor when scaled");
Check(LoginCheckpoints.InitialCharacterCandidate(CharacterGrid(5760, 3600, 4)) == (4600d, 2596d), "large game UI candidate remains anchored without a three-times scale ceiling");
Check(LoginCheckpoints.InitialCharacterCandidate(CharacterGrid(1600, 900, 1))?.X == firstCandidate?.X + 160, "wider game window preserves the grid's right anchor");
Check(LoginCheckpoints.InitialCharacterCandidate(grid with { Lines = [..grid.Lines, grid.Lines[0]] }) == null, "ambiguous footer blocks candidate clicks");
Check(LoginCheckpoints.InitialCharacterCandidate(LoginScreen("Select a character to play", "0 more characters can be made")) == null, "invalid footer geometry blocks candidate clicks");
Check(LoginScreen("Square Enix ID: account123").HasIdentity("account123") && !LoginScreen("Square Enix ID: account1234").HasIdentity("account123"), "SE identity cannot match another ID's prefix");
Check(!LoginScreen("Square Enix ID: acc0unt123").HasIdentity("account123"), "OCR identity ambiguity never permits credentials");
var confirmationButtons = new LoginFrame(1440, 900,
[
    new("Log in with 'Minibubble'?", []),
    new("Yes", [new("Yes", 743, 474.5, 32, 15.5)]),
    new("_ —No", [new("_", 840, 490, 1.333, 1), new("—No", 852, 475, 59.667, 16.333)]),
    new("No", [new("No", 886.333, 475, 25.333, 16.333)])
]);
Check(LoginCheckpoints.ConfirmationButton(confirmationButtons, "No")?.Words is [ { Text: "No", X: 886.333 } ], "overlapping OCR readings resolve to the actual caption, excluding decorative strokes");
Check(LoginCheckpoints.ConfirmationButton(confirmationButtons with { Lines = [..confirmationButtons.Lines, new("No", [new("No", 100, 100, 25, 16)])] }, "No") == null, "physically separate confirmation controls remain ambiguous");
Check(LoginCheckpoints.ConfirmationButton(confirmationButtons with { Lines = [..confirmationButtons.Lines, new("Error code", [])] }, "Yes") == null, "error screens cannot authorize confirmation input");
Check(LoginCheckpoints.ConfirmationButton(confirmationButtons with { Lines = [..confirmationButtons.Lines, new("No", [new("No", double.NaN, 475, 25, 16)])] }, "No") == null, "invalid caption coordinates block confirmation input");
var missingCancelCaption = new LoginFrame(1280, 800, [new("Log in with 'Luckystrikes'?", []), new("No. of content IDs purchased: 1", []), new("Yes", [new("Yes", 662, 419, 30, 14.5)])]);
Check(LoginCheckpoints.ConfirmationCancelPoint(missingCancelCaption, "Luckystrikes") == (805d, 426.25d), "missing cancel caption follows validated paired-button layout");
var scaledCancelCaption = missingCancelCaption with { Width = 2560, Height = 1600, Lines = missingCancelCaption.Lines.Select(l => l with { Words = l.Words.Select(w => w with { X = w.X * 2, Y = w.Y * 2, Width = w.Width * 2, Height = w.Height * 2 }).ToArray() }).ToArray() };
Check(LoginCheckpoints.ConfirmationCancelPoint(scaledCancelCaption, "Luckystrikes") == (1610d, 852.5d), "cancel layout scales with rendered caption geometry");
var fourTimesCancel = missingCancelCaption with { Width = 5120, Height = 3200, Lines = missingCancelCaption.Lines.Select(l => l with { Words = l.Words.Select(w => w with { X = w.X * 4, Y = w.Y * 4, Width = w.Width * 4, Height = w.Height * 4 }).ToArray() }).ToArray() };
Check(LoginCheckpoints.ConfirmationCancelPoint(fourTimesCancel, "Luckystrikes") is {} bigCancel && bigCancel.X > 5120 * .55 && bigCancel.X < 5120 * .8, "large confirmation fallback stays within its validated dialog without a scale ceiling");
Check(LoginCheckpoints.ConfirmationCancelPoint(missingCancelCaption, "Luckystrike") == null, "cancel fallback requires the exact configured prompt");
Check(LoginCheckpoints.ConfirmationCancelPoint(missingCancelCaption with { Lines = [..missingCancelCaption.Lines, new("Error code", [])] }, "Luckystrikes") == null, "error overlay blocks paired-button cancel");
Check(LoginCheckpoints.ConfirmationCancelPoint(confirmationButtons with { Lines = [..confirmationButtons.Lines, new("No", [new("No", 100, 100, 25, 16)])] }, "Minibubble") == null, "ambiguous cancel captions never use geometry fallback");
Check(LoginCheckpoints.ConfirmationCancelPoint(missingCancelCaption with { Lines = [missingCancelCaption.Lines[0], missingCancelCaption.Lines[1], new("Yes", [new("Yes", 50, 419, 30, 14.5)])] }, "Luckystrikes") == null, "paired cancel anchor outside the dialog is rejected");
var noSelectedPrompt = new LoginFrame(1280, 800, [new("Log in with 'Minibubble'?", []), new("No", [new("No", 785, 418, 30, 18)])]);
Check(LoginCheckpoints.Recognize(noSelectedPrompt) == LoginCheckpoint.Unknown && LoginCheckpoints.ConfirmationButton(noSelectedPrompt, "Yes") == null,
    "missing Yes keeps final confirmation blocked");
Check(LoginCheckpoints.ConfirmationCancelPoint(noSelectedPrompt, "Minibubble") == (800d, 427d), "exact-name No-only prompt permits reversible cancellation");
Check(LoginCheckpoints.ConfirmationCancelPoint(noSelectedPrompt, "Minibubbl") == null, "No-only recovery rejects near names");
Check(LoginCheckpoints.ConfirmationCancelPoint(noSelectedPrompt with { Lines = [..noSelectedPrompt.Lines, new("Error code", [])] }, "Minibubble") == null, "No-only recovery rejects errors");
Check(LoginCheckpoints.ConfirmationCancelPoint(noSelectedPrompt with { Lines = [noSelectedPrompt.Lines[0], new("No", [new("No", 20, 20, 30, 18)])] }, "Minibubble") == null, "No-only recovery requires dialog button geometry");
Check(LoginScreen("Play", "Play").UniqueLine("Play") == null, "duplicate action labels cannot authorize an ambiguous click");
var loginAccount = new AccountConfiguration(Guid.NewGuid(), "Test", "", "Mainchar", "Asura", "", "account123");
var splitIdentity = new LoginFrame(640, 480, [new("Crazy 1 forU", [new("Crazy", 416, 228, 40, 12), new("1", 460, 228, 4, 11), new("forU", 468, 228, 28, 11)])]);
Check(splitIdentity.HasIdentity("Crazy1forU"), "adjacent OCR fragments preserve the exact complete account ID");
Check(!splitIdentity.HasIdentity("CrazylforU"), "account ID fragments never substitute lookalike glyphs");
Check(!splitIdentity.HasIdentity("Crazy1") && !splitIdentity.HasIdentity("1forU"), "partial prefixes and suffixes of a fragmented ID cannot match another account");
Check(splitIdentity.IdentityCandidates("Crazy1forU").Length == 1, "split ID yields one physical member candidate");
Check((splitIdentity with { Lines = [..splitIdentity.Lines, ..splitIdentity.Lines] }).IdentityCandidates("Crazy1forU").Length == 1, "overlapping crop reads do not create duplicate member cards");
Check((splitIdentity with { Lines = [..splitIdentity.Lines, new("Crazy1forU", [new("Crazy1forU", 416, 300, 80, 12)])] }).IdentityCandidates("Crazy1forU").Length == 2, "different member cards with the same ID remain ambiguous");
Check(!(splitIdentity with { Lines = [new("Crazy 1 forU", [new("Crazy", 100, 228, 40, 12), new("1", 260, 228, 4, 11), new("forU", 268, 228, 28, 11)])] }).HasIdentity("Crazy1forU"), "separated fields cannot be joined into an account identity");
Check(LoginCheckpoints.Requirements(loginAccount) == null, "configured POL requires no extra member label or POL password");
Check(LoginCheckpoints.Requirements(loginAccount with { UsesOneTimePassword = true }) != null, "OTP requires interactive login");
Check(LoginCheckpoints.Requirements(loginAccount with { World = "Unknown" }) != null, "unknown world blocks unattended identity confirmation");
Console.WriteLine($"{passed} final checks passed.");

if (args.Contains("--ocr-reference"))
{
    var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    foreach (var path in Directory.EnumerateFiles("artifacts/ocr-reference", "*.json"))
    {
        var expected = Enum.Parse<LoginCheckpoint>(Path.GetFileNameWithoutExtension(path).Split('-')[0]);
        // The original Deck captures have unreadable cursive details and a fragmented confirmation prompt.
        if (!path.EndsWith("-windows.json", StringComparison.Ordinal) && (expected is LoginCheckpoint.Characters or LoginCheckpoint.CharacterConfirmation)) expected = LoginCheckpoint.Unknown;
        var frame = System.Text.Json.JsonSerializer.Deserialize<LoginFrame>(File.ReadAllText(path), options)!;
        Check(LoginCheckpoints.Recognize(frame) == expected, "Windows OCR reference checkpoint: " + expected);
        if (path.Contains("MemberList-lucky-cropped", StringComparison.Ordinal))
        {
            Check(frame.IdentityCandidates("Crazy1forU").Length == 1, "enlarged live POL capture locates Luckystrikes' exact split account ID once");
            Check(LoginCheckpoints.MemberCardLabel(frame, "Crazy1forU") == "Luckystrikes", "live exact-ID card binds its unique local member label");
            Check(LoginCheckpoints.MemberCardLabel(frame, "Crazy2forU") == null, "member label cannot repair a wrong account ID");
            var caption = frame.UniqueLine("Luckystrikes")!;
            var variant = caption with { Text = "Luckystlikes", Words = caption.Words.Select(w => w with { Text = "Luckystlikes" }).ToArray() };
            var readings = frame with { Lines = [..frame.Lines, variant] };
            Check(LoginCheckpoints.MemberCardLabels(readings, "Crazy1forU").Contains("Luckystrikes") &&
                LoginCheckpoints.MemberCardLabels(readings, "Crazy1forU").Contains("Luckystlikes"), "overlapping exact-ID caption readings preserve both exact corroboration candidates");
            Check(LoginCheckpoints.MemberCardLabels(readings, "Crazy2forU").Length == 0, "caption variants never repair a wrong SE ID");
            var separate = variant with { Words = variant.Words.Select(w => w with { X = w.X + frame.Width * .25 }).ToArray() };
            Check(LoginCheckpoints.MemberCardLabels(frame with { Lines = [..frame.Lines, separate] }, "Crazy1forU").Length == 0,
                "physically separate member captions cannot share exact-ID selection evidence");
        }
        if (path.Contains("MemberInformation-lucky-current", StringComparison.Ordinal))
        {
            Check(LoginCheckpoints.SelectedMemberLabelMatches(frame, "Luckystrikes"), "live selected-member layout corroborates the previously proven exact-ID card");
            Check(LoginCheckpoints.SelectedMemberLabelMatches(frame, "Lucky strikes"), "OCR spacing changes preserve exact normalized local-caption corroboration");
            Check(!LoginCheckpoints.SelectedMemberLabelMatches(frame, "Luckystlikes"), "caption corroboration never substitutes a similar glyph");
            Check(!LoginCheckpoints.SelectedMemberLabelMatches(frame, "Nanakia"), "another selected member cannot inherit exact-ID evidence");
            var connectBackground = frame with { Lines = [..frame.Lines, new("Connect to PlayOnline", []), new("Square Enix Password", [])] };
            Check(LoginCheckpoints.SelectedMemberLabelBehindConnectMatches(connectBackground, "Luckystrikes"), "cached Log In still corroborates the same-run member behind Connect before credentials");
            Check(!LoginCheckpoints.SelectedMemberLabelBehindConnectMatches(connectBackground, "Nanakia"), "shared Log In control never transfers the teaching account's identity");
            Check(!LoginCheckpoints.SelectedMemberLabelMatches(frame with { Lines = [..frame.Lines, ..frame.Lines.Where(l => l.Text == "Luckystrikes")] }, "Luckystrikes"), "ambiguous member labels cannot corroborate account selection");
        }
        if (expected == LoginCheckpoint.CharacterConfirmation && path.Contains("minibubble", StringComparison.Ordinal))
        {
            Check(LoginCheckpoints.ConfirmsCharacter(frame, "Minibubble") && !LoginCheckpoints.ConfirmsCharacter(frame, "Nanakia"), "captured confirmation preserves exact character identity");
            Check(LoginCheckpoints.ConfirmationButton(frame, "Yes") != null && LoginCheckpoints.ConfirmationButton(frame, "No") != null, "captured overlapping button readings identify both actual captions");
        }
        if (path.Contains("nanakia-fast", StringComparison.Ordinal))
            Check(LoginCheckpoints.ConfirmsCharacter(frame, "Nanakia") && !LoginCheckpoints.ConfirmsCharacter(frame, "Nanikii"), "fast live Nanakia prompt confirms only the default character");
        if (path.Contains("nanakia-conflicting", StringComparison.Ordinal))
            Check(!LoginCheckpoints.ConfirmsCharacter(frame, "Nanakia"), "old redundant crop reproduces Nanakia's conflicting-read failure");
        if (expected == LoginCheckpoint.Characters && path.EndsWith("-windows.json", StringComparison.Ordinal))
        {
            var candidate = LoginCheckpoints.InitialCharacterCandidate(frame);
            Check(candidate != null && candidate.Value.X > 1065 && candidate.Value.X < 1215 && candidate.Value.Y > 637 && candidate.Value.Y < 657, "captured Windows layout anchors inside the first character row");
        }
    }
}

LoginFrame CharacterScreen(params OcrLine[] lines) => new(1920, 1080, lines);
var selected = CharacterScreen(new("Mainchar", [new("Mainchar", 15, 755, 85, 15)]), new("World Asura", [new("World", 15, 790, 40, 15), new("Asura", 60, 790, 40, 15)]));
Check(LoginCheckpoints.SelectedIdentity(selected, "Mainchar", "Asura"), "selected detail fields bind exact character and world");
Check(!LoginCheckpoints.SelectedIdentity(selected, "Mainchar", "Odin"), "selected character on another world cannot authorize world entry");
var listed = CharacterScreen(new("Mainchar", [new("Mainchar", 1580, 850, 85, 15)]), new("World Asura", [new("World", 15, 790, 40, 15), new("Asura", 60, 790, 40, 15)]));
Check(!LoginCheckpoints.SelectedIdentity(listed, "Mainchar", "Asura"), "target name in character list does not prove it is selected");
Check(LoginCheckpoints.ConfirmsCharacter(LoginScreen("Log in with \"Mainchar\"?"), "Mainchar"), "confirmation prompt names exact selected character");
Check(LoginCheckpoints.ConfirmsCharacter(LoginScreen("Log in with", "Log in with \"Mainchar\"?"), "Mainchar"), "instruction-only OCR crop adds no conflicting identity to a complete exact prompt");
Check(!LoginCheckpoints.ConfirmsCharacter(LoginScreen("Log in with"), "Mainchar"), "instruction-only crop cannot authorize world entry");
Check(!LoginCheckpoints.ConfirmsCharacter(LoginScreen("Log in with \"Mainchar\"?", "Log in with \"Main"), "Mainchar"), "partially read character names remain ambiguous and block entry");
Check(!LoginCheckpoints.ConfirmsCharacter(LoginScreen("Log in with \"Otherchar\"?", "Mainchar"), "Mainchar"), "target elsewhere on screen cannot override another character's confirmation");
Check(!LoginCheckpoints.ConfirmsCharacter(LoginScreen("Log in with \"Otherchar\"?", "Log in with \"Mainchar\"?"), "Mainchar"), "conflicting confirmation prompts block world entry");
Check(!LoginCheckpoints.HasConflictingConfirmation(LoginScreen("Log in with", "Log in with \"Mainchar\"?"), "Mainchar"), "incomplete instruction permits waiting for exact confirmation evidence");
Check(LoginCheckpoints.HasConflictingConfirmation(LoginScreen("Log in with \"Otherchar\"?"), "Mainchar"), "another named character is a conflict rather than a transient unreadable frame");
Check(LoginCheckpoints.HasConflictingConfirmation(LoginScreen("Log in with \"Main"), "Mainchar"), "partial character identity never authorizes Enter");
Console.WriteLine($"{passed} final checks passed.");

Check(LoginCheckpoints.Recognize(LoginScreen("Log in with \"Mainchar\"?", "Yes", "No", "Error code")) == LoginCheckpoint.Error, "login errors override character confirmation too");
var misleadingWorld = CharacterScreen(new("Mainchar", [new("Mainchar", 15, 755, 85, 15)]), new("World Odin", [new("World", 15, 790, 40, 15), new("Odin", 60, 790, 40, 15)]), new("Asura", [new("Asura", 15, 820, 40, 15)]));
Check(!LoginCheckpoints.SelectedIdentity(misleadingWorld, "Mainchar", "Asura"), "world must match the selected World field rather than nearby text");
var vanaEpoch = DateTimeOffset.FromUnixTimeSeconds(-92514960);
Check(VanaClock.Read(vanaEpoch).TimeMinutes == 0 && VanaClock.Read(vanaEpoch).Day == "Firesday", "independent Vana calendar epoch starts at Firesday midnight");
Check(VanaClock.Read(vanaEpoch.AddMilliseconds(2400)).TimeMinutes == 1, "one Vana minute advances in 2.4 real seconds without a game process");
Check(VanaClock.Read(vanaEpoch.AddSeconds(3456)).Day == "Earthsday" && VanaClock.Read(vanaEpoch.AddSeconds(3456)).TimeMinutes == 0, "independent clock rolls day at 3456 real seconds");
Check(VanaClock.Read(vanaEpoch.AddSeconds(3456 * 84)).MoonPercent == VanaClock.Read(vanaEpoch).MoonPercent, "moon cycle repeats after 84 Vana days");
Check(WeatherEffect.FromName("Dust storms") == new WeatherEffect("Earth", 1), "dust storm is single Earth weather");
Check(WeatherEffect.FromName("Sand storms") == new WeatherEffect("Earth", 2), "sand storm is double Earth weather");
Check(WeatherEffect.FromName("Squalls") == new WeatherEffect("Water", 2), "squalls render two Water icons");
Check(WeatherEffect.FromName("Sunshine") == new WeatherEffect(null, 0), "clear conditions have no elemental weather icon");
Console.WriteLine($"{passed} final checks passed.");

var artworkAccounts = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
var artworkPool = new[] { "one", "two", "three", "four" };
var firstArtwork = ArtworkRotation.Next(artworkAccounts, artworkPool, null, []);
Check(firstArtwork.Choices.Values.Distinct().Count() == 3, "visible artwork stays unique when the pool is large enough");
var secondArtwork = ArtworkRotation.Next(artworkAccounts, artworkPool, firstArtwork.Choices, firstArtwork.Remaining);
Check(firstArtwork.Choices.Values.Concat(secondArtwork.Choices.Values).Take(4).Distinct().Count() == 4, "rotation uses the entire pool before repeating across shuffles");
Check(secondArtwork.Choices.Values.Distinct().Count() == 3, "cycle boundary does not duplicate visible cards");
var manyArtwork = ArtworkRotation.Next(Enumerable.Range(0, 7).Select(_ => Guid.NewGuid()), artworkPool, null, []);
Check(manyArtwork.Choices.Values.Take(4).Distinct().Count() == 4 && manyArtwork.Choices.Count == 7, "extra accounts reuse artwork only after the pool is exhausted");
Console.WriteLine($"{passed} final checks passed.");

for (var count = 1; count <= 9; count++)
{
    var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
    Dictionary<Guid, string>? prior = null;
    List<string> remaining = [];
    var drawn = new List<string>();
    for (var round = 0; round < 12; round++)
    {
        var rotation = ArtworkRotation.Next(ids, artworkPool, prior, remaining);
        drawn.AddRange(rotation.Choices.Values);
        foreach (var batch in rotation.Choices.Values.Chunk(4))
            if (batch.Distinct().Count() != batch.Length) throw new Exception("Artwork repeats within a visible pool before exhaustion.");
        prior = rotation.Choices; remaining = rotation.Remaining;
    }
    foreach (var cycle in drawn.Chunk(4))
        if (cycle.Distinct().Count() != cycle.Length) throw new Exception("Artwork repeats before a complete saved cycle.");
}
Check(true, "artwork rotation survives repeated cycle boundaries for one through nine accounts");
var artworkDirectory = Path.Combine(Path.GetTempPath(), "ffxi-artwork-state-" + Guid.NewGuid());
try
{
    var artworkStore = new ConfigurationStore(artworkDirectory);
    artworkStore.Save(new LauncherConfiguration(CharacterArtwork: firstArtwork.Choices, ArtworkRemaining: firstArtwork.Remaining));
    Check(artworkStore.Load().ArtworkRemaining!.SequenceEqual(firstArtwork.Remaining), "artwork rotation position survives saved configuration reload");
}
finally { if (Directory.Exists(artworkDirectory)) Directory.Delete(artworkDirectory, true); }
Console.WriteLine($"{passed} final checks passed.");

var alertNow = DateTimeOffset.UtcNow;
var globalCardNotice = new MaintenanceWindow("fixture", "Global", alertNow.AddHours(1), alertNow.AddHours(2));
var worldCardNotice = globalCardNotice with { World = "Asura", StartUtc = alertNow.AddHours(12), EndUtc = alertNow.AddHours(13) };
Check(MaintenanceSchedule.CardAlert([globalCardNotice, worldCardNotice], "Asura", alertNow) == worldCardNotice, "matching-world card alert takes priority at the inclusive 12-hour boundary");
Check(MaintenanceSchedule.CardAlert([globalCardNotice, worldCardNotice], "Odin", alertNow) == globalCardNotice, "other-world maintenance does not replace a global card alert");
Check(MaintenanceSchedule.CardAlert([worldCardNotice], null, alertNow) == null, "unknown world cannot show a specific-world card alert");
Check(MaintenanceSchedule.CardAlert([worldCardNotice with { StartUtc = alertNow.AddHours(12).AddSeconds(1) }], "Asura", alertNow) == null, "card alert stays hidden beyond 12 hours");
Check(MaintenanceSchedule.CardAlert([globalCardNotice with { StartUtc = alertNow.AddHours(-1), EndUtc = alertNow.AddHours(1) }], "Asura", alertNow) != null, "active maintenance remains visible on cards");
Check(MaintenanceSchedule.CardAlert([globalCardNotice with { StartUtc = alertNow.AddHours(-2), EndUtc = alertNow }], "Asura", alertNow) == null, "finished card alerts disappear");
Console.WriteLine($"{passed} final checks passed.");

Check(MaintenanceSchedule.CardAlert([globalCardNotice], "Asura", alertNow, 0) == null, "zero-hour global setting hides upcoming maintenance");
Check(MaintenanceSchedule.CardAlert([worldCardNotice with { StartUtc = alertNow.AddHours(13), EndUtc = alertNow.AddHours(14) }], "Asura", alertNow, 24) != null, "custom 24-hour global setting includes a notice beyond the default horizon");
Check(MaintenanceSchedule.CardAlert([globalCardNotice], "Asura", alertNow, 1) == globalCardNotice, "custom global threshold includes its exact boundary");
Console.WriteLine($"{passed} final checks passed.");
