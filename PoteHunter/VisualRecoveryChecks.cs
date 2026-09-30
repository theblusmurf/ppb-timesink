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
        public long Now {get;private set;}
        public readonly List<long> Opens=[];
        public int Confirms,Finds;
        public int ButtonAfter=1;
        public bool ManualRevival,Stuck,UnknownAfterConfirm;
        public Func<Health>? Read;
        public Action? OnDelay;
        public Health Health()=>Read?.Invoke() ?? (ManualRevival && Opens.Count>0 || Confirms>0&&!Stuck ? new(100,100) : UnknownAfterConfirm&&Confirms>0 ? default : new(0,100));
        public Task<VisualControl?> Find(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();Finds++;
            return Task.FromResult<VisualControl?>(Opens.Count>=ButtonAfter?new(new(500,500),1,new(455,488,91,24),default):null);
        }
        public Task Open(CancellationToken token){token.ThrowIfCancellationRequested();Opens.Add(Now);return Task.CompletedTask;}
        public Task Confirm(VisualControl button,CancellationToken token){token.ThrowIfCancellationRequested();Confirms++;return Task.CompletedTask;}
        public Task Delay(int milliseconds,CancellationToken token){OnDelay?.Invoke();token.ThrowIfCancellationRequested();Now+=milliseconds;return Task.CompletedTask;}
    }
    static async Task Revival()
    {
        var normal=new Surface();await VisualRevival.Run(normal,0,default);
        Require(normal.Opens.SequenceEqual(new[]{2000L})&&normal.Confirms==1,"Revival did not wait two seconds, open, and confirm once.");
        var already=new Surface{Read=()=>new(50,100)};await VisualRevival.Run(already,0,default);
        Require(already.FindCount()==0&&already.Opens.Count==0&&already.Confirms==0,"A living character received revival input.");
        var manual=new Surface{ManualRevival=true};await VisualRevival.Run(manual,0,default);
        Require(manual.Opens.Count==1&&manual.Confirms==0,"Manual revival was followed by another confirmation.");
        var present=new Surface{ButtonAfter=0};await VisualRevival.Run(present,0,default);
        Require(present.Opens.Count==0&&present.Confirms==1,"An already visible Revive button caused an unnecessary opening click.");
        async Task Fails(Surface surface)
        {
            bool failed=false;try{await VisualRevival.Run(surface,0,default);}catch(InvalidOperationException){failed=true;}
            Require(failed,"Unconfirmed revival did not time out.");
        }
        var missing=new Surface{ButtonAfter=10};await Fails(missing);
        Require(missing.Opens.SequenceEqual(new[]{2000L,3000L,4000L})&&missing.Confirms==0,"Missing dialog exceeded three spaced opening attempts.");
        var unreadable=new Surface{Read=()=>default};await Fails(unreadable);
        Require(unreadable.Opens.Count==0&&unreadable.FindCount()==0&&unreadable.Confirms==0,"Unknown health allowed revival input.");
        foreach(bool unknown in new[]{false,true})
        {
            var stuck=new Surface{Stuck=true,UnknownAfterConfirm=unknown};await Fails(stuck);
            Require(stuck.Confirms==1&&stuck.Opens.Count==1,"Unconfirmed living HP repeated a revive click.");
        }
        using var cts=new CancellationTokenSource();var cancel=new Surface{OnDelay=cts.Cancel};bool stopped=false;
        try{await VisualRevival.Run(cancel,0,cts.Token);}catch(OperationCanceledException){stopped=true;}
        Require(stopped&&cancel.Confirms==0&&cancel.Opens.Count==0,"Stop/focus cancellation allowed later revival input.");
    }
    static int FindCount(this Surface surface)=>surface.Finds;
    public static async Task Run()
    {
        var elapsed=Stopwatch.StartNew();Vision();await Revival();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"visual-recovery-checks.json"),JsonSerializer.Serialize(new
        {
            Passed=true,HardwareInputEmitted=false,SyntheticTemplateFixtures=true,ElapsedMilliseconds=elapsed.ElapsedMilliseconds,
            Checks=new[]{"paired repair text and button","moved and scaled inventory","paired Yes confirmation","ambiguous panels rejected","stale hover blocked","centered scaled Revive detection","disappearing and ambiguous Revive blocked","cancellable vision","known dead HP only","manual revival","three spaced opening attempts","one confirmation per death","unknown HP cannot repeat input","focus/stop cancellation"}
        },new JsonSerializerOptions{WriteIndented=true}));
    }
}
