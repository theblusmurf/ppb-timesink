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
        profile.Validate(image.Size);
        Require(profile.Inventory.Matches(image),"A matching repair marker was rejected.");
        RepairVisuals NoAutomatic()=>throw new Exception("Custom repair setup ran the full-screen automatic scan.");
        var customView=LiveRepairSurface.Recognize(image,profile,default,NoAutomatic);
        Require(customView.State.Inventory && customView.State.Hammer && customView.State.Prompt && customView.State.Confirm &&
            customView.Hammer==profile.Hammer.Center && customView.Confirm==profile.Confirm.Center && customView.HammerVisual==null,
            "Saved repair controls were not used directly.");
        using(var tinted=(Bitmap)image.Clone())
        {
            for(int y=0;y<tinted.Height;y++)for(int x=0;x<tinted.Width;x++)
            {
                var pixel=image.GetPixel(x,y);
                tinted.SetPixel(x,y,Color.FromArgb(Math.Min(255,pixel.R+20),Math.Min(255,pixel.G+12),Math.Min(255,pixel.B+16)));
            }
            Require(!profile.Hammer.Matches(tinted) && profile.Hammer.MatchesControl(tinted),
                "A uniform color cast hid the unchanged repair hammer.");
            var tintedView=LiveRepairSurface.Recognize(tinted,profile,default,NoAutomatic);
            Require(tintedView.State.Inventory && tintedView.State.Hammer,"Tinted inventory/hammer blocked repair recognition.");
            using(var g=Graphics.FromImage(tinted))g.FillRectangle(Brushes.Black,profile.Hammer.Bounds);
            var noHammer=LiveRepairSurface.Recognize(tinted,profile,default,NoAutomatic);
            Require(noHammer.State.Inventory && !noHammer.State.Hammer,"Tint compensation accepted a missing hammer.");
        }
        using(var wrongIcon=(Bitmap)image.Clone())
        {
            var bounds=profile.Hammer.Bounds;
            for(int y=bounds.Top;y<bounds.Bottom;y++)for(int x=bounds.Left;x<bounds.Right;x++)
                wrongIcon.SetPixel(x,y,(x/3+y/3)%2==0?Color.FromArgb(15,30,45):Color.FromArgb(230,180,90));
            Require(!profile.Hammer.MatchesControl(wrongIcon),"A different icon matched after color correction.");
        }
        using(var changed=(Bitmap)image.Clone())
        {
            using(var g=Graphics.FromImage(changed))g.FillRectangle(Brushes.Black,profile.Prompt.Bounds);
            Require(!profile.Prompt.Matches(changed) && profile.Inventory.Matches(changed),"A missing repair prompt matched its template.");
            var observed=LiveRepairSurface.Recognize(changed,profile,default,NoAutomatic);
            Require(observed.State.Inventory && observed.State.Hammer && !observed.State.Prompt && !observed.State.Confirm,
                "Saved inventory recognition invented a repair confirmation.");
        }
        using(var plain=new Bitmap(360,90))
        {
            using(var g=Graphics.FromImage(plain))g.Clear(Color.SaddleBrown);
            Invalid(()=>RepairPatch.Capture(plain,new(10,10,40,24)),"A plain background was accepted as a repair control.");
            var missing=LiveRepairSurface.Recognize(plain,profile,default,NoAutomatic);
            Require(missing.State==default,"Missing custom controls triggered automatic scanning or false recognition.");
        }
        // Static white lettering survives a changed translucent background.
        // Require its shape, not just brightness, and retain both dialog gates.
        using(var textImage=new Bitmap(360,90))
        {
            var textArea=new Rectangle(170,45,120,24);
            using(var g=Graphics.FromImage(textImage))
            {g.DrawImageUnscaled(image,0,0);using var background=new SolidBrush(Color.FromArgb(70,60,50));g.FillRectangle(background,textArea);}
            for(int y=49;y<65;y+=3)for(int x=174;x<282;x+=5)
                if((x+y)%4!=0)textImage.SetPixel(x,y,Color.White);
            var textPatch=RepairPatch.Capture(textImage,textArea);
            using var portal=(Bitmap)textImage.Clone();
            for(int y=textArea.Top;y<textArea.Bottom;y++)for(int x=textArea.Left;x<textArea.Right;x++)
                if(textImage.GetPixel(x,y).R<190)portal.SetPixel(x,y,Color.FromArgb(165,120,175));
            Require(!textPatch.Matches(portal) && textPatch.MatchesText(portal),
                "A translucent repair question was rejected when only its background changed.");
            var textInventoryProfile=profile with{Inventory=textPatch};
            var textInventory=LiveRepairSurface.Recognize(portal,textInventoryProfile,default,NoAutomatic);
            Require(textInventory.State.Inventory && textInventory.State.Hammer,
                "Visible inventory lettering was lost over a changed world background.");
            using var absent=(Bitmap)portal.Clone();
            using(var g=Graphics.FromImage(absent))g.FillRectangle(Brushes.Plum,textArea);
            Require(!textPatch.MatchesText(absent),"A blank translucent popup matched repair lettering.");
            using var bright=(Bitmap)portal.Clone();
            using(var g=Graphics.FromImage(bright))g.FillRectangle(Brushes.White,textArea);
            Require(!textPatch.MatchesText(bright),"A bright background was mistaken for repair text.");
            using var different=(Bitmap)portal.Clone();
            using(var g=Graphics.FromImage(different))g.FillRectangle(Brushes.Plum,textArea);
            for(int y=50;y<66;y+=3)for(int x=175;x<283;x+=5)
                if((x+y)%4!=0)different.SetPixel(x,y,Color.White);
            Require(!textPatch.MatchesText(different),"Different lettering matched the repair question.");
            var translucentProfile=profile with{Prompt=textPatch};
            var paired=LiveRepairSurface.Recognize(portal,translucentProfile,default,NoAutomatic);
            Require(paired.State.Prompt && paired.State.Confirm,"Translucent question lost its paired confirmation.");
            using(var g=Graphics.FromImage(portal))g.FillRectangle(Brushes.Black,profile.Confirm.Bounds);
            var noButton=LiveRepairSurface.Recognize(portal,translucentProfile,default,NoAutomatic);
            Require(noButton.State.Prompt && !noButton.State.Confirm,"Recognized text bypassed a missing Yes button.");
        }
        int autoScans=0;
        var automaticView=LiveRepairSurface.Recognize(image,null,default,()=>{autoScans++;return new(null,null);});
        Require(autoScans==1 && automaticView.State==default,"Automatic repair recognition was lost without a custom setup.");
        using(var cancelled=new CancellationTokenSource())
        {
            cancelled.Cancel();bool stopped=false;
            try{LiveRepairSurface.Recognize(image,profile,cancelled.Token,NoAutomatic);}catch(OperationCanceledException){stopped=true;}
            Require(stopped,"Custom recognition ignored cancellation.");
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"repair-profile-priority-checks.json"),
            JsonSerializer.Serialize(new{Passed=true,CustomSetupBypassesFullScreenScan=true,MissingControlsRejected=true,
                AutomaticWithoutProfileRetained=true,CancellationRetained=true}));
        Invalid(()=>RepairPatch.Capture(image,new(-1,10,40,24)),"An out-of-frame control was accepted.");
        var updatedClient=profile with{ClientHash="different-client"};
        updatedClient.Validate(image.Size);
        Require(LiveRepairSurface.Recognize(image,updatedClient,default,NoAutomatic).State==customView.State,
            "A changed executable fingerprint discarded unchanged repair controls.");
        Invalid(()=>(profile with{Version=2}).Validate(image.Size),"An unsupported repair profile format was accepted.");
        using(var resized=new Bitmap(400,90))
            Invalid(()=>LiveRepairSurface.Recognize(resized,profile,default,NoAutomatic),
                "A resized image bypassed saved setup validation with automatic recognition.");
        using(var moved=(Bitmap)image.Clone())
        {
            using(var g=Graphics.FromImage(moved))
            {
                g.FillRectangle(Brushes.Black,profile.Inventory.Bounds);
                g.DrawImage(image,new Rectangle(profile.Inventory.X+1,profile.Inventory.Y+1,
                    profile.Inventory.Width,profile.Inventory.Height),profile.Inventory.Bounds,GraphicsUnit.Pixel);
            }
            var observed=LiveRepairSurface.Recognize(moved,updatedClient,default,NoAutomatic);
            Require(!observed.State.Inventory && !observed.State.Hammer,
                "A moved inventory marker was accepted after a client update.");
        }
        Invalid(()=>profile.Validate(new(400,90)),"A resized game window reused old repair coordinates.");
        Invalid(()=>(profile with{Confirm=profile.Prompt}).Validate(image.Size),"Overlapping prompt/button recognition was accepted.");
        string path=Path.Combine(Path.GetTempPath(),"PoteHunter-repair-"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            Invalid(()=>RepairProfile.Load(image.Size,path),"Missing repair setup was accepted.");
            profile.Save(path);var saved=RepairProfile.Load(image.Size,path);
            Require(saved.Confirm.Matches(image) && saved.Hammer.Center==profile.Hammer.Center,"Saved repair setup lost recognition or click position.");
            var bytes=File.ReadAllBytes(path);
            var reused=RepairProfile.Load(image.Size,path);
            Require(reused.ClientHash=="client" && File.ReadAllBytes(path).SequenceEqual(bytes),
                "Loading a legacy setup rewrote its capture fingerprint or contents.");
            Require(LiveRepairSurface.Recognize(image,reused,default,NoAutomatic).State==customView.State,
                "Legacy saved patches lost recognition after loading.");
            File.WriteAllText(path,"{invalid");Invalid(()=>RepairProfile.Load(image.Size,path),"Corrupt repair setup was accepted.");
            File.WriteAllText(path,"null");Invalid(()=>RepairProfile.Load(image.Size,path),"Empty repair setup was accepted.");
        }
        finally{if(File.Exists(path))File.Delete(path);if(File.Exists(path+".tmp"))File.Delete(path+".tmp");}
        var defaults=JsonSerializer.Deserialize<Options>("{}");
        var options=JsonSerializer.Deserialize<Options>(JsonSerializer.Serialize(new Options{AutoReviveAfterDeath=false,AutoRepairAfterDeath=true}));
        Require(defaults is {AutoRepairAfterDeath:false} && options is {AutoReviveAfterDeath:false,AutoRepairAfterDeath:true},
            "Repair was enabled by default or the independent toggles failed to persist.");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"auto-repair-checks.json"),JsonSerializer.Serialize(new{
            Passed=true,HardwareInputEmitted=false,Checks=new[]{"inventory open/closed starts","one hammer and one confirmation","missing controls and stuck dialogs stop",
                "focus/cancellation/second death stop repair","distinct static patches required","missing prompt rejected","client-update reuse with unchanged UI and strict window/format/patch checks",
                "profile round trip and corrupt data","independent persisted defaults"}
        },new JsonSerializerOptions{WriteIndented=true}));
    }
}
