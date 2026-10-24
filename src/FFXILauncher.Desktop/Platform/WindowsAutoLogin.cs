using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using FFXILauncher.Core;

namespace FFXILauncher.Desktop.Platform;

internal sealed record LoginRequest(string Token, string Nonce, Guid Account, ManagedProcess Process, DateTimeOffset ExpiresUtc, bool InspectOnly = false, bool MemberSectionOnly = false, int PressMs = 80, int PollMs = 50, string TrainingSection = "", int MenuGapMs = 0, bool RecoverDisconnect = false);
internal sealed record LoginReply(string Nonce, string Stage, bool Finished = false, bool Success = false, bool FullFlowValidated = false, LoginCheckpoint? InspectedCheckpoint = null, bool CredentialRejected = false, string? ConfirmedErrorCode = null, bool NeedsFreshRestart = false, bool ConnectionRecovered = false, bool ExternalSessionConflict = false);

public sealed class WindowsAutoLogin(string dataDirectory) : IAutoLoginAutomation, IDisposable
{
    public const string HelperArgument = "--login-worker";
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly SemaphoreSlim gate = new(1);
    private Process? worker;
    private string? sessionDirectory, token;
    internal string? CapturePath => sessionDirectory == null ? null : Path.Combine(sessionDirectory, "frame.bmp");
    internal LoginCheckpoint? LastInspectionCheckpoint { get; private set; }
    internal bool LastInspectionCredentialRejected { get; private set; }
    internal string? LastInspectionErrorCode { get; private set; }
    internal bool LastRecoveryNeedsRestart { get; private set; }
    internal bool LastConnectionRecovered { get; private set; }
    internal string? LastSectionResult { get; private set; }
    public event Action<Guid, string>? Progress;
    internal event Action<Guid>? ExternalSessionDetected;
    internal static void ValidateRecoveryCompletion(bool requested, bool recovered, bool restartRequired)
    {
        if (requested && recovered == restartRequired)
            throw new LoginSafetyException("Disconnect recovery stopped before a verified world entry or fresh restart was established.");
    }
    public string? SetupProblem(AccountConfiguration account) => LoginCheckpoints.Requirements(account) ??
        (!WindowsUnattendedSetup.IsElevated ? "Start the manager using its unattended desktop shortcut." : null);
    public string? ReadinessProblem(AccountConfiguration account) => account.RecoveryPauseReason ?? SetupProblem(account) ??
        (!WindowsLoginValidation.IsValidated(dataDirectory, account) ? "Validate this account's complete Windows login first." : null);
    public async Task LoginAsync(AccountConfiguration account, ManagedProcess process, ICredentialVault vault, CancellationToken cancellationToken)
    {
        if (ReadinessProblem(account) is {} problem) throw new InvalidOperationException(problem);
        await RequestAsync(account, process, false, cancellationToken);
    }
    internal async Task<bool> RequestAsync(AccountConfiguration account, ManagedProcess process, bool inspect, CancellationToken cancellation, bool memberSectionOnly = false, int pressMs = 80, int pollMs = 50, string trainingSection = "", int menuGapMs = 0, bool recoverDisconnect = false)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var canonical = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FFXILauncher");
        if (!string.Equals(Path.GetFullPath(dataDirectory), canonical, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Login automation requires the Windows user's normal configuration directory.");
        await gate.WaitAsync(cancellation);
        string? nonce = null;
        var externalHandled = false;
        try
        {
            await EnsureWorker(cancellation);
            nonce = Guid.NewGuid().ToString("N");
            LastSectionResult = null;
            LastInspectionCheckpoint = null;
            LastInspectionCredentialRejected = false;
            LastInspectionErrorCode = null;
            LastRecoveryNeedsRestart = false;
            LastConnectionRecovered = false;
            var duration = !inspect && !memberSectionOnly && trainingSection == "" && !recoverDisconnect ? GameUpdateWorkflow.MaximumLoginDuration : TimeSpan.FromMinutes(8);
            var deadline = DateTimeOffset.UtcNow + duration;
            WriteJson(Path.Combine(sessionDirectory!, "request.json"), new LoginRequest(token!, nonce, account.Id, process, deadline, inspect, memberSectionOnly, pressMs, pollMs, trainingSection, menuGapMs, recoverDisconnect));
            string? stage = null;
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellation.ThrowIfCancellationRequested();
                if (worker!.HasExited) throw new InvalidOperationException("The login helper closed. Check Windows UAC and the Windows user account.");
                var reply = ReadJson<LoginReply>(Path.Combine(sessionDirectory!, "reply.json"));
                if (reply?.Nonce == nonce)
                {
                    if (reply.Stage != stage) { stage = reply.Stage; Progress?.Invoke(account.Id, stage); }
                    if (reply.Finished)
                    {
                        if (reply.ExternalSessionConflict) { externalHandled = true; ExternalSessionDetected?.Invoke(account.Id); }
                        if (!reply.Success) throw new InvalidOperationException(reply.Stage);
                        if (memberSectionOnly || trainingSection != "") LastSectionResult = reply.Stage;
                        LastInspectionCheckpoint = reply.InspectedCheckpoint;
                        LastInspectionCredentialRejected = reply.CredentialRejected;
                        LastInspectionErrorCode = reply.ConfirmedErrorCode;
                        LastRecoveryNeedsRestart = reply.NeedsFreshRestart;
                        LastConnectionRecovered = reply.ConnectionRecovered;
                        return reply.FullFlowValidated;
                    }
                }
                await Task.Delay(200, cancellation);
            }
            throw new TimeoutException("Automatic login timed out. Inspect the managed POL window before retrying.");
        }
        finally
        {
            if (nonce != null && sessionDirectory != null && Directory.Exists(sessionDirectory))
                try { File.WriteAllText(Path.Combine(sessionDirectory, "cancel"), nonce); } catch (IOException) { }
            // The worker persists the policy before input. Reconcile it even
            // when its final reply was lost or the helper unexpectedly exited.
            try
            {
                var saved = new ConfigurationStore(dataDirectory).Load().ConfiguredAccounts.SingleOrDefault(a => a.Id == account.Id);
                if (!externalHandled && saved is { MaintainConnection: false, RecoveryPauseReason: not null })
                    ExternalSessionDetected?.Invoke(account.Id);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
            finally { gate.Release(); }
        }
    }
    private async Task EnsureWorker(CancellationToken cancellation)
    {
        if (worker is { HasExited: false }) return;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (!WindowsUnattendedSetup.IsElevated)
            throw new InvalidOperationException("Start the manager from its unattended desktop shortcut. Use Enable unattended access once to install it.");
        var session = Guid.NewGuid().ToString("N");
        sessionDirectory = Path.Combine(dataDirectory, "automation", session);
        WindowsPrivateDirectory.Create(sessionDirectory);
        token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        File.WriteAllText(Path.Combine(sessionDirectory, "token"), token);
        using var identity = WindowsIdentity.GetCurrent(); using var parent = Process.GetCurrentProcess();
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { HelperArgument, session, parent.Id.ToString(), parent.StartTime.ToUniversalTime().Ticks.ToString(), identity.User!.Value }) info.ArgumentList.Add(arg);
        try { worker = Process.Start(info) ?? throw new InvalidOperationException("Windows could not start the login helper."); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { throw new InvalidOperationException("Approve the Windows UAC prompt to automate the elevated POL session."); }
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (!File.Exists(Path.Combine(sessionDirectory, "ready")))
        {
            cancellation.ThrowIfCancellationRequested();
            if (worker.HasExited) throw new InvalidOperationException("The login helper rejected startup (exit " + worker.ExitCode + "). Check the original Windows user and parent process identity.");
            if (DateTimeOffset.UtcNow > deadline) throw new InvalidOperationException("The login helper initialization timed out.");
            await Task.Delay(100, cancellation);
        }
    }
    internal static void WriteJson<T>(string path, T value)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value));
            var deadline = Stopwatch.StartNew();
            while (true)
            {
                try { File.Move(temporary, path, true); break; }
                // Windows readers without FileShare.Delete briefly prevent
                // replacement. Do not drop the completion reply on that race.
                catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && deadline.Elapsed < TimeSpan.FromSeconds(1)) { Thread.Sleep(25); }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static T? ReadJson<T>(string path)
    {
        try { if (!File.Exists(path) || new FileInfo(path).Length > 16384) return default; return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return default; }
    }
    public void Dispose()
    {
        // Revoking the capability also cancels input and causes the elevated worker to exit.
        if (sessionDirectory != null) try { File.Delete(Path.Combine(sessionDirectory, "token")); } catch (IOException) { }
        worker?.Dispose();
    }
    public static int RunCalibration(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args.Length != 5 || args[4] != "--interactive" || !int.TryParse(args[2], out var pid) || !long.TryParse(args[3], out var ticks)) return 2;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FFXILauncher");
        var calibration = Path.Combine(root, "calibration");
        var result = Path.Combine(calibration, "result.json");
        var command = Path.Combine(calibration, "command.json");
        var session = Guid.NewGuid().ToString("N");
        WindowsPrivateDirectory.Create(calibration);
        try
        {
            File.Delete(command);
            var account = new ConfigurationStore(root).Load().ConfiguredAccounts.Single(a => a.Character.Equals(args[1], StringComparison.OrdinalIgnoreCase));
            using var client = new WindowsAutoLogin(root);
            client.Progress += (_, stage) => WriteJson(result, new CalibrationResult(session, false, stage));
            var owned = new ManagedProcess(pid, new DateTime(ticks, DateTimeKind.Utc));
            void Capture()
            {
                client.RequestAsync(account, owned, true, CancellationToken.None).GetAwaiter().GetResult();
                File.Copy(client.CapturePath!, Path.Combine(calibration, "client.bmp"), true);
            }
            Capture();
            WriteJson(result, new CalibrationResult(session, true, "Calibration ready; captured verified POL client."));
            var deadline = DateTimeOffset.UtcNow.AddMinutes(30);
            while (DateTimeOffset.UtcNow < deadline)
            {
                var instruction = ReadJson<CalibrationCommand>(command);
                if (instruction?.Session == session)
                {
                    File.Delete(command);
                    if (instruction.Operation == "exit") return 0;
                    if (instruction.Operation is not ("inspect" or "login")) continue;
                    string stage = "Captured the verified POL client.";
                    try
                    {
                        if (instruction.Operation == "login") client.RequestAsync(account, owned, false, CancellationToken.None).GetAwaiter().GetResult();
                    }
                    catch (InvalidOperationException ex) { stage = ex.Message; }
                    Capture();
                    WriteJson(result, new CalibrationResult(session, true, stage));
                }
                Thread.Sleep(250);
            }
            return 0;
        }
        catch (Exception ex)
        {
            WriteJson(result, new CalibrationResult(session, true, "Calibration stopped: " + (ex is InvalidOperationException ? ex.Message : ex.GetType().Name)));
            return 1;
        }
    }
    internal sealed record CalibrationCommand(string Session, string Operation);
    internal sealed record CalibrationResult(string Session, bool Waiting, string Message);
    public static int RunWorker(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args.Length != 5 || args[0] != HelperArgument || !Guid.TryParseExact(args[1], "N", out _) || !int.TryParse(args[2], out var parentId) || !long.TryParse(args[3], out var parentTicks)) return 2;
        using var identity = WindowsIdentity.GetCurrent(); if (identity.User?.Value != args[4]) return 3;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FFXILauncher");
        var directory = Path.Combine(root, "automation", args[1]);
        var ownsSession = false;
        try
        {
            using var parent = Process.GetProcessById(parentId);
            if (parent.StartTime.ToUniversalTime().Ticks != parentTicks || !string.Equals(WindowsSessionPlatform.ExecutablePath(parentId), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return 4;
            var token = File.ReadAllText(Path.Combine(directory, "token")); if (token.Length != 64) return 5;
            ownsSession = true;
            File.WriteAllText(Path.Combine(directory, "ready"), "ready");
            string? handled = null;
            while (!parent.HasExited && File.Exists(Path.Combine(directory, "token")))
            {
                var request = ReadJson<LoginRequest>(Path.Combine(directory, "request.json"));
                if (request != null && request.Nonce != handled && Guid.TryParseExact(request.Nonce, "N", out _) && request.Token == token && request.ExpiresUtc > DateTimeOffset.UtcNow && request.ExpiresUtc <= DateTimeOffset.UtcNow + (!request.InspectOnly && !request.MemberSectionOnly && request.TrainingSection == "" && !request.RecoverDisconnect ? GameUpdateWorkflow.MaximumLoginDuration : TimeSpan.FromMinutes(8)))
                {
                    handled = request.Nonce;
                    using var cancelled = new CancellationTokenSource();
                    using var monitor = new System.Threading.Timer(_ =>
                    {
                        try { if (parent.HasExited || !File.Exists(Path.Combine(directory, "token")) || File.Exists(Path.Combine(directory, "cancel")) && File.ReadAllText(Path.Combine(directory, "cancel")) == request.Nonce || DateTimeOffset.UtcNow > request.ExpiresUtc) cancelled.Cancel(); }
                        catch (IOException) { try { cancelled.Cancel(); } catch (ObjectDisposedException) { } }
                        catch (ObjectDisposedException) { }
                    }, null, 0, 100);
                    void Report(string stage) => WriteJson(Path.Combine(directory, "reply.json"), new LoginReply(request.Nonce, stage));
                    try
                    {
                        if (request.TrainingSection is not ("" or "password" or "play" or "continue" or "character" or "return" or "success" or "clear-panel" or "external-exit") ||
                            request.RecoverDisconnect && (request.InspectOnly || request.MemberSectionOnly || request.TrainingSection != "") ||
                            request.InspectOnly && (request.MemberSectionOnly || request.TrainingSection != "") ||
                            request.MemberSectionOnly && request.TrainingSection != "") throw new LoginSafetyException("Conflicting calibration modes rejected.");
                        if (request.PressMs is < 1 or > 80 || request.PollMs is < 1 or > 50 ||
                            !request.MemberSectionOnly && request.TrainingSection is not ("character" or "success") && (request.PressMs != 80 || request.PollMs != 50))
                            throw new LoginSafetyException("Timing settings outside bounded section training rejected.");
                        if (request.TrainingSection == "success" ? request.MenuGapMs is < 0 or > 3000 : request.MenuGapMs != 0) throw new LoginSafetyException("Menu gap outside bounded success training rejected.");
                        var config = new ConfigurationStore(root).Load(); var account = config.ConfiguredAccounts.Single(a => a.Id == request.Account);
                        if ((request.RecoverDisconnect || request.TrainingSection == "clear-panel") && (!account.MaintainConnection || !account.AutoLoginEnabled))
                            throw new LoginSafetyException("Maintain connection was disabled; recovery cancelled.");
                        if (!request.InspectOnly && request.TrainingSection != "external-exit" && account.RecoveryPauseReason is {} pausedReason)
                            throw new LoginSafetyException(pausedReason);
                        var engine = new WindowsLoginEngine(directory, config, account, request.Process, Report);
                        engine.RunAsync(new WindowsCredentialVault(Path.Combine(root, "vault")), request.InspectOnly, cancelled.Token, request.MemberSectionOnly, request.PressMs, request.PollMs, request.TrainingSection, request.MenuGapMs, request.RecoverDisconnect).GetAwaiter().GetResult();
                        ValidateRecoveryCompletion(request.RecoverDisconnect, engine.ConnectionRecovered, engine.NeedsFreshRestart);
                        WriteJson(Path.Combine(directory, "reply.json"), new LoginReply(request.Nonce, request.InspectOnly ? "Calibration capture saved." : engine.NeedsFreshRestart ? "A further error prevented reconnection; a fresh POL process is required." : request.MemberSectionOnly || request.TrainingSection != "" ? engine.MemberSectionResult ?? "Section completed without a full login proof." : "Character and world verified online.", true, true, !request.InspectOnly && !request.MemberSectionOnly && request.TrainingSection == "" && engine.FullFlowValidated, engine.InspectedCheckpoint, engine.InspectedCredentialRejection, engine.InspectedErrorCode, engine.NeedsFreshRestart, engine.ConnectionRecovered));
                    }
                    catch (Exception ex)
                    {
                        // Never return OCR text, credentials, or detailed native exceptions across IPC.
                        var message = ex is OperationCanceledException ? "Login cancelled." : ex is LoginSafetyException ? ex.Message : "Login helper stopped (" + ex.GetType().Name + "). Inspect POL before retrying.";
                        WriteJson(Path.Combine(directory, "reply.json"), new LoginReply(request.Nonce, message, true, ExternalSessionConflict: ex is ExternalSessionException));
                    }
                }
                Thread.Sleep(100);
            }
            return 0;
        }
        catch { return 6; }
        finally { try { if (ownsSession && Directory.Exists(directory)) Directory.Delete(directory, true); } catch (IOException) { } }
    }
}
internal class LoginSafetyException(string message) : InvalidOperationException(message);
internal sealed class LoginFocusException(string message) : LoginSafetyException(message);
internal sealed class LoginObscuredException(string message) : LoginSafetyException(message);
internal sealed class LoginWindowUnavailableException(string message) : LoginSafetyException(message);

internal sealed class ExternalSessionException(string message) : LoginSafetyException(message);
