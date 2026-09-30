using System.Text.Json;

namespace PoteHunter;

internal static class AutoRepairChecks
{
    sealed class Surface(bool inventoryOpen=false) : IRepairSurface
    {
        int stage=inventoryOpen?1:0;
        public readonly List<RepairAction> Actions=[];
        public bool NoInventory,NoHammer,UnknownPrompt,NoConfirm,StuckPrompt,StuckInventory;
        public Exception? AfterHammer;
        public Action? OnDelay;
        public Task<RepairObservation> Observe(CancellationToken token)
        {
            if(stage==2 && AfterHammer!=null)throw AfterHammer;
            return Task.FromResult<RepairObservation>(stage switch
            {
                1 or 3=>new(true,!NoHammer,false,false),
                2=>new(true,false,!UnknownPrompt,!NoConfirm),
                _=>default
            });
        }
        public Task Perform(RepairAction action,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();Actions.Add(action);
            stage=action switch
            {
                RepairAction.OpenInventory=>NoInventory?0:1,
                RepairAction.Hammer=>2,
                RepairAction.Confirm=>StuckPrompt?2:3,
                RepairAction.CloseInventory=>StuckInventory?3:4,
                _=>throw new Exception("Unexpected repair action")
            };
            return Task.CompletedTask;
        }
        public Task Delay(CancellationToken token){OnDelay?.Invoke();token.ThrowIfCancellationRequested();return Task.CompletedTask;}
    }

    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    static void Invalid(Action action,string message)
    {
        bool failed=false;try{action();}catch(InvalidOperationException){failed=true;}
        Require(failed,message);
    }
    public static async Task Run()
    {
        var complete=new Surface();await AutoRepair.Run(complete,default);
        Require(complete.Actions.SequenceEqual(new[]{RepairAction.OpenInventory,RepairAction.Hammer,RepairAction.Confirm,RepairAction.CloseInventory}),
            "Repair did not open inventory, repair once, then close inventory.");
        var open=new Surface(true);await AutoRepair.Run(open,default);
        Require(open.Actions.SequenceEqual(complete.Actions.Skip(1)),"Repair toggled an already open inventory closed.");
        var alreadyPrompt=new Surface();await alreadyPrompt.Perform(RepairAction.Hammer,default);alreadyPrompt.Actions.Clear();
        async Task Fails(Surface surface,params RepairAction[] actions)
        {
            bool failed=false;try{await AutoRepair.Run(surface,default);}catch(InvalidOperationException){failed=true;}
            Require(failed && surface.Actions.SequenceEqual(actions),"Repair continued or repeated input after an unrecognized/stuck UI state.");
        }
        await Fails(alreadyPrompt);
        await Fails(new(){NoInventory=true},RepairAction.OpenInventory);
        await Fails(new(){NoHammer=true},RepairAction.OpenInventory);
        await Fails(new(){UnknownPrompt=true},RepairAction.OpenInventory,RepairAction.Hammer);
        await Fails(new(){NoConfirm=true},RepairAction.OpenInventory,RepairAction.Hammer);
        await Fails(new(){StuckPrompt=true},RepairAction.OpenInventory,RepairAction.Hammer,RepairAction.Confirm);
        await Fails(new(){StuckInventory=true},complete.Actions.ToArray());
        foreach(var failure in new Exception[]{new OperationCanceledException("Focus lost"),new DeathRecoveryRequiredException()})
        {
            var interrupted=new Surface{AfterHammer=failure};Exception? actual=null;
            try{await AutoRepair.Run(interrupted,default);}catch(Exception ex){actual=ex;}
            Require(ReferenceEquals(actual,failure) && interrupted.Actions.SequenceEqual(complete.Actions.Take(2)),
                "Repair swallowed focus loss or a second death, or confirmed afterward.");
        }
        using(var cancellation=new CancellationTokenSource())
        {
            var interrupted=new Surface{NoHammer=true,OnDelay=cancellation.Cancel};bool cancelled=false;
            try{await AutoRepair.Run(interrupted,cancellation.Token);}catch(OperationCanceledException){cancelled=true;}
            Require(cancelled && interrupted.Actions.SequenceEqual(complete.Actions.Take(1)),"Repair ignored cancellation while waiting for a control.");
        }

        // Generated UI patches exercise pixel recognition without capturing a
        // screen or sending input to the user's running game.
        using var image=new Bitmap(360,90);
        for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)
            image.SetPixel(x,y,(x/3+y/3)%2==0?Color.FromArgb(230,180,90):Color.FromArgb(15,30,45));
        var profile=new RepairProfile(1,"client",image.Width,image.Height,
            RepairPatch.Capture(image,new(10,10,40,24)),RepairPatch.Capture(image,new(90,10,40,24)),
            RepairPatch.Capture(image,new(170,10,40,24)),RepairPatch.Capture(image,new(250,10,40,24)));
        profile.Validate("CLIENT",image.Size);
        Require(profile.Inventory.Matches(image),"A matching repair marker was rejected.");
        using(var changed=(Bitmap)image.Clone())
        {
            using(var g=Graphics.FromImage(changed))g.FillRectangle(Brushes.Black,profile.Prompt.Bounds);
            Require(!profile.Prompt.Matches(changed) && profile.Inventory.Matches(changed),"A missing repair prompt matched its template.");
        }
        using(var plain=new Bitmap(360,90))
        {
            using(var g=Graphics.FromImage(plain))g.Clear(Color.SaddleBrown);
            Invalid(()=>RepairPatch.Capture(plain,new(10,10,40,24)),"A plain background was accepted as a repair control.");
        }
        Invalid(()=>RepairPatch.Capture(image,new(-1,10,40,24)),"An out-of-frame control was accepted.");
        Invalid(()=>profile.Validate("different-client",image.Size),"A repair setup from another client was accepted.");
        Invalid(()=>profile.Validate("client",new(400,90)),"A resized game window reused old repair coordinates.");
        Invalid(()=>(profile with{Confirm=profile.Prompt}).Validate("client",image.Size),"Overlapping prompt/button recognition was accepted.");
        string path=Path.Combine(Path.GetTempPath(),"PoteHunter-repair-"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            Invalid(()=>RepairProfile.Load("client",image.Size,path),"Missing repair setup was accepted.");
            profile.Save(path);var saved=RepairProfile.Load("client",image.Size,path);
            Require(saved.Confirm.Matches(image) && saved.Hammer.Center==profile.Hammer.Center,"Saved repair setup lost recognition or click position.");
            File.WriteAllText(path,"{invalid");Invalid(()=>RepairProfile.Load("client",image.Size,path),"Corrupt repair setup was accepted.");
            File.WriteAllText(path,"null");Invalid(()=>RepairProfile.Load("client",image.Size,path),"Empty repair setup was accepted.");
        }
        finally{if(File.Exists(path))File.Delete(path);if(File.Exists(path+".tmp"))File.Delete(path+".tmp");}
        var defaults=JsonSerializer.Deserialize<Options>("{}");
        var options=JsonSerializer.Deserialize<Options>(JsonSerializer.Serialize(new Options{AutoReviveAfterDeath=false,AutoRepairAfterDeath=true}));
        Require(defaults is {AutoRepairAfterDeath:false} && options is {AutoReviveAfterDeath:false,AutoRepairAfterDeath:true},
            "Repair was enabled by default or the independent toggles failed to persist.");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"auto-repair-checks.json"),JsonSerializer.Serialize(new{
            Passed=true,HardwareInputEmitted=false,Checks=new[]{"inventory open/closed starts","one hammer and one confirmation","missing controls and stuck dialogs stop",
                "focus/cancellation/second death stop repair","distinct static patches required","missing prompt rejected","client/window compatibility",
                "profile round trip and corrupt data","independent persisted defaults"}
        },new JsonSerializerOptions{WriteIndented=true}));
    }
}
