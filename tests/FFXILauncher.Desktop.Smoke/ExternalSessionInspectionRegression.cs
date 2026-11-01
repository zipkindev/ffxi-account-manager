using FFXILauncher.Core;
using FFXILauncher.Desktop.Platform;

internal static class ExternalSessionInspectionRegression
{
    internal static void Run()
    {
        if (!LoginFlowCompatibility.AcceptsPriorFlow("19984AD8C6D8CF8EB5D789720D26D4DE7FDC62D7F7214E9ED49B90CA8D7BDD92") ||
            !LoginFlowCompatibility.AcceptsPriorFlow("172A72FFDD972E983D42031D67E38ED84DC73700B512855640DD9071AC382EB5") ||
            LoginFlowCompatibility.AcceptsPriorFlow(new string('0', 64)))
            throw new Exception("The inspected external-session fix must preserve reviewed normal-login proofs and reject unknown flows.");
        static LoginFrame Frame(params string[] lines) => new(640, 480,
            lines.Select((text, i) => new OcrLine(text, [new(text, 100, 150 + i * 30, 500, 20)])).ToArray());
        var conflict = Frame("Error code: POL-1039", "Disconnected from PlayOnline.",
            "The current PlayOnline ID was used to log in from another terminal.", "OK");
        var normal = Frame("Select character");
        var root = Path.Combine(Path.GetTempPath(), "ffxi-inspected-external-" + Guid.NewGuid().ToString("N"));
        try
        {
            var target = new AccountConfiguration(Guid.NewGuid(), "Target", "Profile", "Target", "Asura", "Member", "SE",
                MaintainConnection: true, AutoLogin: true, PlayOnlineId: "test-pol");
            var other = target with { Id = Guid.NewGuid(), Character = "Other", PlayOnlineId = "other-pol" };
            var store = new ConfigurationStore(root);
            var initial = new LauncherConfiguration(Accounts: [target, other]) { Theme = "ffxi" };
            store.Save(initial);
            var before = File.ReadAllBytes(Path.Combine(root, "accounts.json"));
            foreach (var (first, second) in new[] { (conflict, normal), (normal, conflict),
                (Frame("POL-1039", "Unrelated HUD text"), conflict),
                (conflict, Frame("FFXI-3201", "Same character is already logged in.")) })
                if (WindowsLoginEngine.PersistExternalSessionConflict(root, target, first, second) ||
                    !before.SequenceEqual(File.ReadAllBytes(Path.Combine(root, "accounts.json"))))
                    throw new Exception("Unconfirmed or changed external-session evidence must not change recovery settings.");
            try
            {
                WindowsLoginEngine.PersistExternalSessionConflict(root, target with { PlayOnlineId = "changed" }, conflict, conflict);
                throw new Exception("Changed account mapping must refuse policy mutation.");
            }
            catch (LoginSafetyException) { }
            if (!before.SequenceEqual(File.ReadAllBytes(Path.Combine(root, "accounts.json"))))
                throw new Exception("Rejected mapping must leave settings intact.");
            // No process or cleanup worker is needed: persist at the inspection boundary,
            // then simulate POL disappearing and a new manager reading the policy.
            if (!WindowsLoginEngine.PersistExternalSessionConflict(root, target, conflict, conflict))
                throw new Exception("Two confirmed external-session reads must pause recovery immediately.");
            var saved = new ConfigurationStore(root).Load();
            var paused = saved.ConfiguredAccounts.Single(a => a.Id == target.Id);
            if (paused.MaintainConnection || paused.RecoveryPauseReason != AccountConfiguration.ExternalSessionReason ||
                !paused.AutoLogin || saved.ConfiguredAccounts.Single(a => a.Id == other.Id) != other || saved.Theme != initial.Theme)
                throw new Exception("The saved pause must survive process exit and preserve other accounts, manual login and settings.");
            if (WindowsAutoLogin.ExternalPauseProblem(paused, false) == null || WindowsAutoLogin.ExternalPauseProblem(paused, true) != null)
                throw new Exception("Automatic restart must remain blocked while explicit manual override stays available.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        Console.WriteLine("PASS: confirmed POL-1039 inspection persists recovery pause before cleanup or process exit; rejected reads and other accounts preserved.");
    }
}
