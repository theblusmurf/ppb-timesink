using System.Diagnostics;
using System.Text.Json;

namespace PoteHunter;

internal static class VisualRecoveryChecks
{
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    static Bitmap Frame(int width=1280,int height=900)
    {
        var image=new Bitmap(width,height);using var g=Graphics.FromImage(image);g.Clear(Color.FromArgb(29,47,63));return image;
    }
    static void Draw(Bitmap frame,string name,Point position,double scale=1)
    {
        using var source=RecoveryVision.Resource(name);
        using var resized=new Bitmap(source,new Size((int)Math.Round(source.Width*scale),(int)Math.Round(source.Height*scale)));
        using var g=Graphics.FromImage(frame);g.DrawImageUnscaled(resized,position);
    }
    static Point Offset(Point point,int x,int y,double scale)=>new(point.X+(int)Math.Round(x*scale),point.Y+(int)Math.Round(y*scale));
    static void Inventory(Bitmap frame,Point hammer,double scale=1,bool marker=true)
    {
        Draw(frame,"RepairHammer",hammer,scale);
        if(marker)Draw(frame,"RepairInventory",Offset(hammer,-284,-586,scale),scale);
    }
    static void Prompt(Bitmap frame,Point buttons,double scale=1,bool marker=true)
    {
        Draw(frame,"RepairButtons",buttons,scale);
        if(marker)Draw(frame,"RepairQuestion",Offset(buttons,-155,-27,scale),scale);
    }
    static bool Near(Point? actual,Point expected)=>actual is {} point && Math.Abs(point.X-expected.X)<=2 && Math.Abs(point.Y-expected.Y)<=2;
    static void Vision()
    {
        using(var blank=Frame())
        {
            Require(RecoveryVision.Repair(blank,default)==new RepairVisuals(null,null) && RecoveryVision.Revive(blank,default)==null,
                "Blank frame was mistaken for a recovery control.");
            Inventory(blank,new(470,650),marker:false);Prompt(blank,new(770,430),marker:false);
            Require(RecoveryVision.Repair(blank,default)==new RepairVisuals(null,null),"Repair button without its matching text was accepted.");
        }
        foreach(double scale in new[]{1d,.75})
        {
            using var image=Frame();var hammer=new Point(470,650);Inventory(image,hammer,scale);
            var first=RecoveryVision.Repair(image,default);
            Require(Near(first.Hammer?.Point,Offset(hammer,13,24,scale)) && first.Confirm==null,
                $"Moved/scaled inventory ({scale}) was not recognized at its actual button position: {first}.");
            Require(RecoveryVision.RepairMarker(image,first.Hammer!,false,default),"Inventory disappeared during a stable hover check.");
            var buttons=new Point(770,430);Prompt(image,buttons,scale);
            var both=RecoveryVision.Repair(image,default);
            Require(Near(both.Confirm?.Point,Offset(buttons,34,16,scale)),"Repair Yes was not identified from the paired dialog text.");
            Require(!RecoveryVision.RepairMarker(image,first.Hammer!,false,default),"A newly opened prompt did not block a stale hammer click.");
            Require(RecoveryVision.RepairMarker(image,both.Confirm!,true,default),"Repair question failed the pre-click recheck.");
            using(var g=Graphics.FromImage(image))g.FillRectangle(Brushes.Black,both.Confirm!.Marker);
            Require(!RecoveryVision.RepairMarker(image,both.Confirm!,true,default),"A removed repair question still authorized confirmation.");
        }
        using(var ambiguous=Frame())
        {
            Inventory(ambiguous,new(400,650));Inventory(ambiguous,new(900,650));
            Prompt(ambiguous,new(430,340));Prompt(ambiguous,new(930,430));
            Require(RecoveryVision.Repair(ambiguous,default)==new RepairVisuals(null,null),"Duplicate repair panels were not rejected.");
        }
        foreach(var (height,scale) in new[]{(1440,1d),(1080,.75)})
        {
            using var image=Frame(1920,height);var corner=new Point(960-(int)Math.Round(91*scale)/2,height/2+12);
            Draw(image,"ReviveButton",corner,scale);
            var found=RecoveryVision.Revive(image,default);
            Require(Near(found?.Point,Offset(corner,45,12,scale)),"Centered/scaled Revive button was not recognized.");
            Require(RecoveryVision.ReviveStillPresent(image,found!,default),"Stable Revive button failed its final recheck.");
            using(var hover=(Bitmap)image.Clone())
            {
                for(int y=found!.Button.Top;y<found.Button.Bottom;y++)for(int x=found.Button.Left;x<found.Button.Right;x++)
                {
                    var c=hover.GetPixel(x,y);
                    if(Math.Min(c.R,Math.Min(c.G,c.B))<190)hover.SetPixel(x,y,Color.FromArgb(Math.Min(180,c.R+40),Math.Min(180,c.G+40),Math.Min(180,c.B+40)));
                }
                Require(RecoveryVision.ReviveStillPresent(hover,found,default) && RecoveryVision.Revive(hover,default)!=null,
                    "Revive hover hid unchanged foreground text.");
            }
            using(var g=Graphics.FromImage(image))g.FillRectangle(Brushes.Black,found!.Button);
            Require(!RecoveryVision.ReviveStillPresent(image,found!,default),"Disappeared Revive button still authorized a click.");
        }
        using(var unrelated=Frame(1920,1440))
        {
            Draw(unrelated,"ReviveButton",new(60,60));
            Require(RecoveryVision.Revive(unrelated,default)==null,"Non-dialog Revive label was accepted outside the center search area.");
            Draw(unrelated,"ReviveButton",new(915,682));Draw(unrelated,"ReviveButton",new(915,752));
            Require(RecoveryVision.Revive(unrelated,default)==null,"Two candidate Revive buttons were accepted.");
        }
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();using var large=Frame(1920,1080);
        bool stopped=false;try{RecoveryVision.Repair(large,cancelled.Token);}catch(OperationCanceledException){stopped=true;}
        Require(stopped,"Cancelled image recognition continued searching.");
    }

    sealed class Surface : IRevivalSurface
    {
        public long Now {get;set;}
        public readonly List<long> Opens=[];
        public readonly List<long> ConfirmedAt=[];
        public readonly List<long> HealthChecksAfterConfirm=[];
        public int Confirms,Finds;
        public int OpenCalls,ConfirmCalls,RefusedOpenings,RefusedConfirmations;
        public int ButtonAfter=1;
        public bool ManualRevival,Stuck,UnknownAfterConfirm;
        public bool DialogDisappears;
        public int MissedFramesAfterRefusal;
        public Func<Health>? Read;
        public Action? OnDelay;
        public Health Health()
        {
            if(Confirms>0)HealthChecksAfterConfirm.Add(Now);
            return Read?.Invoke() ?? (ManualRevival && Opens.Count>0 || Confirms>0&&!Stuck ? new(100,100) : UnknownAfterConfirm&&Confirms>0 ? default : new(0,100));
        }
        public Task<VisualControl?> Find(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();Finds++;
            if(ConfirmCalls>0 && MissedFramesAfterRefusal-->0)return Task.FromResult<VisualControl?>(null);
            return Task.FromResult<VisualControl?>(Opens.Count>=ButtonAfter && !(DialogDisappears&&ConfirmCalls>0)?new(new(500,500),1,new(455,488,91,24),default):null);
        }
        public Task<bool> Open(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if(++OpenCalls<=RefusedOpenings)return Task.FromResult(false);
            Opens.Add(Now);return Task.FromResult(true);
        }
        public Task<bool> Confirm(VisualControl button,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if(++ConfirmCalls<=RefusedConfirmations)return Task.FromResult(false);
            Confirms++;ConfirmedAt.Add(Now);return Task.FromResult(true);
        }
        public Task Delay(int milliseconds,CancellationToken token){OnDelay?.Invoke();token.ThrowIfCancellationRequested();Now+=milliseconds;return Task.CompletedTask;}
    }
    static async Task Revival()
    {
        Require(VisualRevival.OpeningPoint(new(1920,1080))==new Point(960,540) &&
            VisualRevival.OpeningPoint(new(801,601))==new Point(400,300),"Opening click did not use the current game-window centre.");
        var normal=new Surface();await VisualRevival.Run(normal,0,default);
        Require(normal.Opens.SequenceEqual(new[]{2000L})&&normal.Confirms==1,"Revival did not wait two seconds, open, and confirm once.");
        var triple=new Surface{ButtonAfter=3};await VisualRevival.Run(triple,0,default);
        Require(triple.Opens.SequenceEqual(new[]{2000L,3000L,4000L})&&triple.Confirms==1,
            "Three-click death screen did not receive three opening clicks after the two-second wait.");
        var shifted=new Surface{ButtonAfter=3,RefusedOpenings=3};await VisualRevival.Run(shifted,0,default);
        Require(shifted.Opens.SequenceEqual(new[]{5000L,6000L,7000L})&&shifted.OpenCalls==6&&shifted.Confirms==1,
            "A shifted pointer consumed opening clicks or cancelled recovery before three real clicks.");
        var changedDialog=new Surface{ButtonAfter=0,RefusedConfirmations=2};await VisualRevival.Run(changedDialog,0,default);
        Require(changedDialog.Opens.Count==0&&changedDialog.ConfirmCalls==3&&changedDialog.ConfirmedAt.SequenceEqual(new[]{2200L}),
            "A changing dialog was not rechecked before the single confirmation click.");
        var flicker=new Surface{ButtonAfter=0,RefusedConfirmations=1,MissedFramesAfterRefusal=3};
        await VisualRevival.Run(flicker,0,default);
        Require(flicker.Opens.Count==0 && flicker.Confirms==1 && flicker.ConfirmedAt.Single()==2400,
            "Transient lost recognition aborted revival or repeated opening clicks.");
        var delayed=new Surface{Now=10000,ButtonAfter=3};await VisualRevival.Run(delayed,0,default);
        Require(delayed.Opens.SequenceEqual(new[]{10000L,11000L,12000L})&&delayed.Confirms==1,
            "An elapsed saved-route delay gained another death wait.");
        var laterDeath=new Surface{ButtonAfter=3};await VisualRevival.Run(laterDeath,1000,default);
        Require(laterDeath.Opens.SequenceEqual(new[]{3000L,4000L,5000L}),"Opening clicks were not timed from the observed death.");
        var already=new Surface{Read=()=>new(50,100)};await VisualRevival.Run(already,0,default);
        Require(already.FindCount()==0&&already.Opens.Count==0&&already.Confirms==0,"A living character received revival input.");
        var manual=new Surface{ManualRevival=true};await VisualRevival.Run(manual,0,default);
        Require(manual.Opens.Count==1&&manual.Confirms==0,"Manual revival was followed by another confirmation.");
        var present=new Surface{ButtonAfter=0};await VisualRevival.Run(present,0,default);
        Require(present.Opens.Count==0&&present.ConfirmedAt.SequenceEqual(new[]{2000L}),
            "An already visible Revive button bypassed the death wait or caused an unnecessary opening click.");
        async Task Fails(Surface surface)
        {
            bool failed=false;try{await VisualRevival.Run(surface,0,default);}catch(InvalidOperationException){failed=true;}
            Require(failed,"Unconfirmed revival did not time out.");
        }
        var missing=new Surface{ButtonAfter=10};await Fails(missing);
        Require(missing.Opens.SequenceEqual(new[]{2000L,3000L,4000L})&&missing.Confirms==0&&missing.Now>=5000,
            "Missing dialog exceeded three opening clicks or did not wait for the last click to settle.");
        var unreadable=new Surface{Read=()=>default};await Fails(unreadable);
        Require(unreadable.Opens.Count==0&&unreadable.FindCount()==0&&unreadable.Confirms==0,"Unknown health allowed revival input.");
        var unstable=new Surface{RefusedOpenings=int.MaxValue};await Fails(unstable);
        Require(unstable.Opens.Count==0&&unstable.Confirms==0&&unstable.Now<=18000,
            "Unstable pointer retries were unbounded or emitted a click.");
        var stale=new Surface{ButtonAfter=0,RefusedConfirmations=int.MaxValue};await Fails(stale);
        Require(stale.Opens.Count==0&&stale.Confirms==0&&stale.Now<=18000,
            "Stale confirmation retries emitted input or exceeded the recovery deadline.");
        var disappeared=new Surface{ButtonAfter=0,RefusedConfirmations=1,DialogDisappears=true};await Fails(disappeared);
        Require(disappeared.Opens.Count==0&&disappeared.Confirms==0,"Lost dialog recognition restarted opening clicks.");
        Require(disappeared.Now<=17000,"Lost recognition exceeded its bounded recheck deadline.");
        foreach(int stopAfter in new[]{1,2})
        {
            var recovered=new Surface{ButtonAfter=3};
            recovered.Read=()=>recovered.Opens.Count>=stopAfter?new(100,100):new(0,100);
            await VisualRevival.Run(recovered,0,default);
            Require(recovered.Opens.Count==stopAfter&&recovered.Confirms==0,"Opening sequence clicked after manual revival.");
            var lostHealth=new Surface{ButtonAfter=3};
            lostHealth.Read=()=>lostHealth.Opens.Count>=stopAfter?default:new(0,100);
            await Fails(lostHealth);
            Require(lostHealth.Opens.Count==stopAfter&&lostHealth.Confirms==0,"Opening sequence clicked after HP became unreadable.");
            using var stop=new CancellationTokenSource();var interrupted=new Surface{ButtonAfter=3};
            interrupted.OnDelay=()=>{if(interrupted.Opens.Count>=stopAfter)stop.Cancel();};
            bool cancelled=false;
            try{await VisualRevival.Run(interrupted,0,stop.Token);}catch(OperationCanceledException){cancelled=true;}
            Require(cancelled&&interrupted.Opens.Count==stopAfter&&interrupted.Confirms==0,
                "Stop/focus cancellation failed to interrupt the opening sequence.");
        }
        foreach(bool unknown in new[]{false,true})
        {
            var stuck=new Surface{Stuck=true,UnknownAfterConfirm=unknown};await Fails(stuck);
            Require(stuck.Confirms==1&&stuck.Opens.Count==1,"Unconfirmed living HP repeated a revive click.");
            Require(stuck.HealthChecksAfterConfirm.Count==76 && stuck.HealthChecksAfterConfirm.Last()-stuck.ConfirmedAt.Single()==15000 &&
                stuck.HealthChecksAfterConfirm.Zip(stuck.HealthChecksAfterConfirm.Skip(1)).All(p=>p.Second-p.First==200),
                "Revival confirmation did not poll HP every 200 ms for exactly 15 seconds.");
        }
        var boundary=new Surface();boundary.Read=()=>boundary.Confirms>0&&boundary.Now-boundary.ConfirmedAt.Single()>=15000?new(1,100):new(0,100);
        await VisualRevival.Run(boundary,0,default);
        Require(boundary.Confirms==1&&boundary.HealthChecksAfterConfirm.Last()-boundary.ConfirmedAt.Single()==15000,"Living HP at the confirmation deadline was missed.");
        using var cts=new CancellationTokenSource();var cancel=new Surface{OnDelay=cts.Cancel};bool stopped=false;
        try{await VisualRevival.Run(cancel,0,cts.Token);}catch(OperationCanceledException){stopped=true;}
        Require(stopped&&cancel.Confirms==0&&cancel.Opens.Count==0,"Stop/focus cancellation allowed later revival input.");
    }
    static async Task ZeroHpReturn()
    {
        var state=new DeathRecoveryState();
        bool interrupted=false;
        try{DeathRecoveryState.InterruptIfDead(new(0,15370),true,hp=>state.Observe(hp,0));}
        catch(DeathRecoveryRequiredException){interrupted=true;}
        Require(interrupted&&state.Pending,"Zero HP did not transfer control from an activity to recovery.");
        var surface=new Surface{ButtonAfter=3,RefusedOpenings=2};
        await VisualRevival.Run(surface,state.ObservedAt,default);
        state.Observe(surface.Health(),surface.Now);
        Require(state.Pending&&surface.Confirms==1,"Repositioning or living HP discarded the pending anchor return.");
        var primary=new SavedNavigationRoute(1,new(20,0),0,[new(20,0),new(16,0),new(8,0),new(0,0)],DateTime.UtcNow,"Farmer",100);
        var alternative=primary with{Anchor=new(20,8),Points=[new(20,8),new(16,8),new(8,8),new(0,8),new(0,0)]};
        SavedNavigationRoute?[] routes=[primary,alternative,null];
        var fallback=new RecoveryFallbackCycle(0);fallback.Reject(0);
        int slot=fallback.Select(3,i=>routes[i] is {} r&&RecoveryTravel.SharedOrigin(primary,r),i=>i==0);
        Require(slot==1,"Recovery did not choose the free alternative after a zero-HP revival.");
        var plan=RecoveryTravel.Plan(routes,alternative,new(0,0),false);
        var path=new RecoveryPath(plan.Points,alternative.Anchor);
        foreach(var point in plan.Points){Require(state.Pending,"Return cleared before arrival.");path.Next(point);}
        Require(path.Next(alternative.Anchor)==null,"Revival fallback did not finish at the alternative anchor.");
        state.Reset();Require(!state.Pending,"Arrival failed to complete zero-HP recovery.");
    }
    static int FindCount(this Surface surface)=>surface.Finds;
    public static async Task Run()
    {
        var elapsed=Stopwatch.StartNew();Vision();await Revival();await RevivalSetupChecks.Run();await ZeroHpReturn();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"visual-recovery-checks.json"),JsonSerializer.Serialize(new
        {
            Passed=true,HardwareInputEmitted=false,SyntheticTemplateFixtures=true,ElapsedMilliseconds=elapsed.ElapsedMilliseconds,
            Checks=new[]{"paired repair text and button","moved and scaled inventory","paired Yes confirmation","ambiguous panels rejected","stale hover blocked","centered scaled Revive detection","disappearing and ambiguous Revive blocked","cancellable vision","known dead HP only","two-second death wait including visible dialog","three opening clicks with one-second gaps","elapsed saved delay respected","observed death timing","manual revival interrupts opening sequence","unknown HP interrupts opening sequence","no fourth opening click","one confirmation per death","unknown HP cannot repeat input","focus/stop cancellation between opening clicks","shifted pointer retries do not consume actual clicks","stale dialog rechecked before confirmation","bounded retries without blind clicks","zero HP through revival and alternative-route arrival"}
        },new JsonSerializerOptions{WriteIndented=true}));
    }
}
