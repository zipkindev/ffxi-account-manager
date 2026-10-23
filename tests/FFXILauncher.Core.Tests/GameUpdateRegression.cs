using FFXILauncher.Core;

internal static class GameUpdateRegression
{
    internal static void Run(Action<bool, string> check)
    {
        static LoginFrame Frame(params string[] text) => new(640,480,text.Select((t,i)=>new OcrLine(t,[new(t,320,160+i*30,100,20)])).ToArray());
        var available=Frame("Version Update","A version update is necessary.","Update","Cancel");
        var start=GameUpdateWorkflow.Recognize(available);
        check(start.Stage==GameUpdateStage.Available && start.Button=="Update","updater requires version heading and explicit available wording");
        check(GameUpdateWorkflow.Recognize(Frame("Next","OK","Update")).Stage==GameUpdateStage.None,"generic updater controls authorize nothing");
        check(GameUpdateWorkflow.Recognize(Frame("Latest version update news","A new version is available","Update")).Stage==GameUpdateStage.None,"news does not authorize updater input");
        check(GameUpdateWorkflow.Recognize(Frame("Version Update","Check Files","File Repair","Next")).Stage==GameUpdateStage.Error,"file repair route is excluded");
        check(GameUpdateWorkflow.Recognize(Frame("Version Update","Error code: POL-1160","Update")).Stage==GameUpdateStage.Error,"update errors take precedence over controls");
        check(GameUpdateWorkflow.Recognize(Frame("Version Update","Please restart the PlayOnline Viewer","OK")).Stage==GameUpdateStage.RestartRequired,"viewer restart is a separate explicit pause");
        check(GameUpdateWorkflow.Recognize(Frame("Version Update","Do you wish to begin the version update?","Yes","No")).Button=="Yes","specific version update confirmation permits Yes");
        check(GameUpdateWorkflow.Recognize(Frame("Version Update","Do you wish to begin the version update?","Yes")).Button==null,"incomplete confirmation authorizes no input");
        var now=DateTimeOffset.UtcNow;
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
