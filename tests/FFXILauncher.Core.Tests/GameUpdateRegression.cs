using FFXILauncher.Core;
using System.Text.Json;

internal static class GameUpdateRegression
{
    internal static void Run(Action<bool, string> check)
    {
        static LoginFrame Frame(params string[] text) => new(640,480,text.Select((t,i)=>new OcrLine(t,[new(t,320,160+i*30,100,20)])).ToArray());
        foreach (var (fixture,code) in new[]{("overlaid-4001","FFXI-4001"),("overlaid-3100","FFXI-3100"),("version-network-0019","POL-0019")})
        {
            var captured=JsonSerializer.Deserialize<LoginFrame>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures",fixture+".json")))!;
            check(LoginCheckpoints.Recognize(captured)==LoginCheckpoint.Error && LoginCheckpoints.NetworkDialogCode(captured)==code && LoginCheckpoints.MaintenanceErrorCode(captured)==code,"captured OCR recognizes the specific network dialog despite overlays: "+code);
            if(code!="POL-0019")check(LoginCheckpoints.DisconnectDialogCode(captured)==code,"captured overlay permits confirmed disconnect handling: "+code);
            else check(GameUpdateWorkflow.Control(captured,"Retry")!=null,"captured POL-0019 Retry has unique bounded geometry");
            var footer=captured with {Lines=captured.Lines.Select(l=>l with {Words=l.Words.Select(w=>w with {Y=captured.Height*.9}).ToArray()}).ToArray()};
            check(LoginCheckpoints.NetworkDialogCode(footer)==null,"footer text cannot authorize recovery: "+code);
        }
        check(LoginCheckpoints.NetworkDialogCode(Frame("FFXI-4001","Unrelated HUD text"))==null,"a standalone error number cannot authorize network recovery");
        check(LoginCheckpoints.NetworkDialogCode(Frame("FFXI-3100","Select character"))==null,"3100 requires the specific lobby failure wording");
        check(LoginCheckpoints.NetworkDialogCode(Frame("POL-0019","Cannot connect to server","network settings"))==null,"POL-0019 requires the exact version communication heading");
        check(LoginCheckpoints.MaintenanceErrorCode(Frame("Error code: FFXI-4001","Error code: POL-5311"))==null,"conflicting codes cannot authorize cleanup");
        var elsewhere=Frame("Error code: POL-1039","Disconnected from PlayOnline.","The current PlayOnline ID was used to log in from another terminal.","OK");
        check(LoginCheckpoints.IsExternalSessionConflict(elsewhere),"POL-1039 requires its specific other-terminal message");
        check(!LoginCheckpoints.IsExternalSessionConflict(Frame("Error code: FFXI-3201","Same character is already logged in.")),"a temporary held character session does not disable recovery");
        check(!LoginCheckpoints.IsExternalSessionConflict(Frame("POL-1039","Unrelated HUD text")),"a bare 1039 code cannot authorize local session cleanup");
        check(!LoginCheckpoints.IsExternalSessionConflict(elsewhere with {Lines=elsewhere.Lines.Select(l=>l with {Words=l.Words.Select(w=>w with {Y=470}).ToArray()}).ToArray()}),"footer external-login text cannot authorize cleanup");
        check(!DisconnectRecovery.IsDisconnectCode("POL-1039") && DisconnectRecovery.Followup("POL-1039","POL-1039",LoginCheckpoint.Error)==DisconnectFollowup.Wait,"external-terminal conflicts never queue a fresh automatic login");
        var pausedAccount=new AccountConfiguration(Guid.NewGuid(),"Test","Test","Test","Asura","Test","Test",MaintainConnection:true,AutoLogin:true);
        var otherAccount=pausedAccount with {Id=Guid.NewGuid(),Character="Other"};
        var pauseRoot=Path.Combine(Path.GetTempPath(),"ffxi-external-session-"+Guid.NewGuid().ToString("N"));
        try {
            var store=new ConfigurationStore(pauseRoot);
            store.Save(new LauncherConfiguration(Accounts:[pausedAccount.PauseForExternalSession(),otherAccount]));
            var reloaded=new ConfigurationStore(pauseRoot).Load();
            var paused=reloaded.ConfiguredAccounts.Single(a=>a.Id==pausedAccount.Id);
            check(!paused.MaintainConnection && paused.AutoLogin && paused.RecoveryPauseReason==AccountConfiguration.ExternalSessionReason,"external login disables only Maintain connection and persists its visible reason across app restarts");
            check(reloaded.ConfiguredAccounts.Single(a=>a.Id==otherAccount.Id)==otherAccount,"external login does not change another account's settings");
            check(!RecoveryPolicy.Evaluate(paused,false,new MaintenanceSnapshot([],DateTimeOffset.UtcNow),DateTimeOffset.UtcNow).Allowed,"persisted external-session policy suppresses automatic recovery");
        } finally {if(Directory.Exists(pauseRoot))Directory.Delete(pauseRoot,true);}
        var network=new NetworkRetryBackoff();
        check(network.ReserveRetry()==TimeSpan.FromSeconds(30) && network.ReserveRetry()==TimeSpan.FromSeconds(60) && network.ReserveRetry()==TimeSpan.FromSeconds(120),"version communication retries use increasing 30/60/120-second waits");
        try{network.ReserveRetry();check(false,"network retry must stop after three attempts");}catch(InvalidOperationException){check(network.Attempts==3,"network retry exhaustion retains the bounded attempt count");}
        var console=JsonSerializer.Deserialize<LoginFrame>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","windower-console.json")))!;
        check(LoginCheckpoints.HasEmptyWindowerConsole(console),"captured Windower console requires empty prompt and addon history");
        check(!LoginCheckpoints.HasEmptyWindowerConsole(console with {Lines=console.Lines.Where(l=>!l.Text.StartsWith("Addon:")).ToArray()}),"a dollar glyph without console context permits no command");
        check(!LoginCheckpoints.HasEmptyWindowerConsole(console with {Lines=console.Lines.Select(l=>l.Text.StartsWith("$")?l with {Text="$existing text"}:l).ToArray()}),"nonempty console prompt permits no command");
        check(!LoginCheckpoints.HasEmptyWindowerConsole(console with {Lines=console.Lines.Select(l=>l with {Words=l.Words.Select(w=>w with {Y=console.Height*.9}).ToArray()}).ToArray()}),"chat-footer prompt cannot authorize console input");
        var panel=JsonSerializer.Deserialize<LoginFrame>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","obscuring-panel.json")))!;
        check(LoginCheckpoints.HasHaupPanel(panel) && !LoginCheckpoints.HasHaupPanel(Frame("HAUP")) && !LoginCheckpoints.HasHaupPanel(Frame("All original options")),"specific HaupBridge title and control label identify the optional panel together");
        var ipcRoot=Path.Combine(Path.GetTempPath(),"ffxi-status-sharing-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ipcRoot);
        try{
            var heartbeatNow=DateTimeOffset.UtcNow;var account=new AccountConfiguration(Guid.NewGuid(),"Test","","Test","Asura","","");
            var path=Path.Combine(ipcRoot,"asura_test.status");
            File.WriteAllText(path,$"0123456789abcdef\nTest\nAsura\n{heartbeatNow.ToUnixTimeSeconds()}\n{heartbeatNow.ToUnixTimeSeconds()}\n");
            using(var locked=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                check(new ShutdownIpc(ipcRoot).ReadSession(account,heartbeatNow)==null,"a temporarily locked render heartbeat is unavailable evidence rather than a launcher crash");
            check(new ShutdownIpc(ipcRoot).ReadSession(account,heartbeatNow)!=null,"heartbeat reading recovers when the writer releases its file");
        }finally{Directory.Delete(ipcRoot,true);}
        var terminated=Frame("Error code: POL-0512","Connection terminated or not available.","Please try connecting again later or check cables, networking devices, and network settings.","OK");
        check(LoginCheckpoints.NetworkDialogCode(terminated)=="POL-0512" && LoginCheckpoints.DisconnectDialogCode(terminated)=="POL-0512","a terminated POL session permits only its specific confirmed network recovery");
        check(DisconnectRecovery.IsDisconnectCode("POL-0512") && LoginCheckpoints.NetworkDialogCode(Frame("POL-0512","Unrelated failure"))==null,"0512 requires its specific connection-termination wording");
        var held=Frame("Error code: FFXI-3201","Same character is already logged in.","Please wait a few minutes before trying to reconnect.","OK");
        check(CharacterSessionBackoff.IsHeld(held),"3201 requires numeric code and held-session wording");
        check(!CharacterSessionBackoff.IsHeld(Frame("Error code: FFXI-3202","Same character is already logged in.","Please wait a few minutes before trying to reconnect.")),"different error code cannot authorize character retry");
        check(!CharacterSessionBackoff.IsHeld(Frame("Error code: FFXI-3201","Incorrect password")),"3201 without held-session wording cannot authorize retry");
        var backoff=new CharacterSessionBackoff();
        for(var attempt=0;attempt<CharacterSessionBackoff.MaximumRetries;attempt++)check(backoff.ReserveRetry()==TimeSpan.FromMinutes(1),"held session retries wait one minute");
        try{backoff.ReserveRetry();check(false,"held session retries must be bounded");}catch(InvalidOperationException){check(backoff.Attempts==5,"held-session retry exhaustion preserves the bound");}
        check(CharacterSessionBackoff.RequiresCrashDelay(false,false),"unexpected exit requires server-session cooldown");
        check(!CharacterSessionBackoff.RequiresCrashDelay(true,false),"confirmed disconnect cleanup keeps its established restart path");
        check(!CharacterSessionBackoff.RequiresCrashDelay(false,true),"scheduled maintenance restart does not add a crash delay");
        check(CharacterSessionBackoff.CrashRestartDelay==TimeSpan.FromMinutes(2),"crash cooldown uses the initial two-minute trial setting");
        var available=Frame("Version Update","A version update is necessary.","Update","Cancel");
        var start=GameUpdateWorkflow.Recognize(available);
        check(GameUpdateWorkflow.Recognize(Frame("Version Update","Update","Cancel")).Button=="Update","official updater heading and exact Update control work with partial body OCR");
        check(GameUpdateWorkflow.Recognize(Frame("Version Update","Update complete.","OK")).Stage==GameUpdateStage.Complete,"explicit compact update completion permits OK");
        check(start.Stage==GameUpdateStage.Available && start.Button=="Update","updater requires version heading and explicit available wording");
        check(GameUpdateWorkflow.Recognize(Frame("Next","OK","Update")).Stage==GameUpdateStage.None,"generic updater controls authorize nothing");
        check(GameUpdateWorkflow.Recognize(Frame("Latest version update news","A new version is available","Update")).Stage==GameUpdateStage.None,"news does not authorize updater input");
        check(GameUpdateWorkflow.Recognize(Frame("Version Update","Check Files","File Repair","Next")).Stage==GameUpdateStage.Error,"file repair route is excluded");
        check(GameUpdateWorkflow.Recognize(Frame("Version Update","Error code: POL-1160","Update")).Stage==GameUpdateStage.Error,"update errors take precedence over controls");
        check(GameUpdateWorkflow.Recognize(Frame("Version Update","Please restart the PlayOnline Viewer","OK")).Stage==GameUpdateStage.RestartRequired,"viewer restart is a separate explicit pause");
        check(GameUpdateWorkflow.Recognize(Frame("Version Update","Do you wish to begin the version update?","Yes","No")).Button=="Yes","specific version update confirmation permits Yes");
        check(GameUpdateWorkflow.Recognize(Frame("Version Update","Do you wish to begin the version update?","Yes")).Button==null,"incomplete confirmation authorizes no input");
        var now=DateTimeOffset.UtcNow;
        var owner=new ManagedProcess(41,now.UtcDateTime);
        var other=owner with {Pid=42};
        check(!GameUpdateWorkflow.MayClaim(other,owner,true),"unfinished live updater prevents another account from patching after a timeout");
        check(GameUpdateWorkflow.MayClaim(owner,owner,true),"the exact updater owner can resume in place");
        check(GameUpdateWorkflow.MayClaim(other,owner,false),"confirmed exited owner releases updater eligibility");
        check(!GameUpdateWorkflow.MayClaim(owner with{StartedUtc=owner.StartedUtc.AddSeconds(1)},owner,true),"PID reuse cannot inherit an updater lease");
        var flow=new GameUpdateWorkflow(now);
        flow.Observe(start,now);
        check(!flow.ShouldClick(start,true),"other running games prevent patch writes");
        check(flow.ShouldClick(start,false),"recognized update can start after other games exit");
        flow.ActionSent(start);
        check(!flow.ShouldClick(start,false),"unchanged updater screen cannot repeat a dispatched action");
        flow.Observe(new(GameUpdateStage.None),now.AddSeconds(5));
        flow.Observe(start,now.AddSeconds(10));
        check(!flow.ShouldClick(start,false),"partial OCR miss cannot rearm a dispatched updater action");
        var downloading=GameUpdateWorkflow.Recognize(Frame("Version Update","Downloading files","1 / 100","10%","ROM0.DAT"));
        check(downloading.Stage==GameUpdateStage.Downloading && downloading.Button==null,"download progress never sends input");
        flow.Observe(downloading,now.AddMinutes(1));
        flow.Observe(downloading with {Progress="2/100"},now.AddMinutes(15));
        check(true,"measurable file progress extends the stall budget");
        try{flow.Observe(downloading with {Progress="2/100"},now.AddMinutes(30));check(false,"stalled update must stop");}catch(InvalidOperationException){check(true,"15-minute unchanged progress stops updater");}
        var cap=new GameUpdateWorkflow(now);
        try{cap.Observe(downloading,now+GameUpdateWorkflow.MaximumDuration);check(false,"hard updater deadline must stop");}catch(InvalidOperationException){check(true,"hard two-hour deadline stops even progressing updater");}
        check(GameUpdateWorkflow.Recognize(Frame("Version Update","Checking files","30/100")).Stage==GameUpdateStage.Checking,"file checking is recognized");
        check(GameUpdateWorkflow.Recognize(Frame("Version Update","Installing files","30%")).Stage==GameUpdateStage.Installing,"installing is recognized");
        var complete=GameUpdateWorkflow.Recognize(Frame("Version Update","The version update is complete.","Next"));
        check(complete.Stage==GameUpdateStage.Complete && complete.Button=="Next","completion requires explicit wording and unique control");
        check(GameUpdateWorkflow.Recognize(Frame("Version Update","The version update is complete.","Next","Next")).Button==null,"ambiguous completion control cannot be clicked");
        var bad=available with {Lines=available.Lines.Select(l=>l.Text=="Update"?l with{Words=[new("Update",double.NaN,300,100,20)]}:l).ToArray()};
        check(GameUpdateWorkflow.Recognize(bad).Button==null,"invalid OCR geometry cannot authorize update input");
        check(GameUpdateWorkflow.MaximumLoginDuration>GameUpdateWorkflow.MaximumDuration,"request envelope covers update and normal login");
    }
}
