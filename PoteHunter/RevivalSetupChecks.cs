using System.Text.Json;

namespace PoteHunter;

internal static class RevivalSetupChecks
{
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    static void Invalid(Action action,string message)
    {
        bool failed=false;try{action();}catch(InvalidOperationException){failed=true;}
        Require(failed,message);
    }
    internal static Bitmap Fixture(bool popup)
    {
        var image=new Bitmap(960,640);using var g=Graphics.FromImage(image);g.Clear(Color.FromArgb(35,40,45));
        using var font=new Font("Segoe UI",14);
        g.DrawString("CHARACTER DEFEATED",font,Brushes.White,70,100);
        if(popup)
        {
            g.FillRectangle(Brushes.DarkSlateGray,350,200,330,170);
            g.DrawString("RETURN TO LIFE?",font,Brushes.White,370,220);
            g.FillRectangle(Brushes.DimGray,435,280,100,36);
            g.DrawString("Revive",font,Brushes.White,452,284);
        }
        return image;
    }
    internal static RevivalProfile Profile(Bitmap before,Bitmap popup)=>new(1,"fixture-client",popup.Width,popup.Height,
        new(RepairPatch.Capture(before,new(65,95,260,35)),null,480,320),
        new(RepairPatch.Capture(popup,new(365,215,240,35)),RepairPatch.Capture(popup,new(455,282,60,28)),485,296));

    sealed class Surface(RevivalProfile profile,Bitmap before,Bitmap popup,int openingsNeeded=1) : IRevivalSurface
    {
        public long Now {get;private set;}
        public readonly List<string> Actions=[];
        public readonly List<long> OpenedAt=[];
        int stage;
        public Health Health()=>stage==2?new(100,100):new(0,100);
        public Task<VisualControl?> Find(CancellationToken token)=>Task.FromResult(profile.Find(stage==0?before:popup,token));
        public Task<bool> Open(CancellationToken token)
        {
            Require(stage==0&&profile.CanOpen(before,token),"Custom opening action lacked its death-screen marker.");
            Actions.Add("open");OpenedAt.Add(Now);
            if(OpenedAt.Count>=openingsNeeded)stage=1;
            return Task.FromResult(true);
        }
        public Task<bool> Confirm(VisualControl control,CancellationToken token)
        {
            Require(stage==1&&profile.CanConfirm(popup,control,token),"Custom confirmation lacked its dialog marker.");
            Actions.Add("confirm");stage=2;return Task.FromResult(true);
        }
        public Task Delay(int milliseconds,CancellationToken token){token.ThrowIfCancellationRequested();Now+=milliseconds;return Task.CompletedTask;}
    }
    public static async Task Run()
    {
        using var before=Fixture(false);using var popup=Fixture(true);var profile=Profile(before,popup);
        profile.Validate("fixture-client",popup.Size);
        Require(profile.CanOpen(before,default)&&profile.Find(before,default)==null,"Death screen was mistaken for a Revive confirmation.");
        var found=profile.Find(popup,default);
        Require(found is {Custom:true}&&found.Point==new Point(485,296)&&!profile.CanOpen(popup,default),"Saved confirmation was not preferred over the opening step.");
        Require(!(profile with{Opening=null}).CanOpen(before,default),"Dialog-only setup invented an opening click.");
        var surface=new Surface(profile,before,popup);await VisualRevival.Run(surface,0,default);
        Require(surface.Actions.SequenceEqual(new[]{"open","confirm"}),"Custom revival did not open, confirm once, then wait for living HP.");
        var triple=new Surface(profile,before,popup,3);await VisualRevival.Run(triple,0,default);
        Require(triple.Actions.SequenceEqual(new[]{"open","open","open","confirm"})&&triple.OpenedAt.SequenceEqual(new[]{3000L,3250L,3500L}),
            "Custom opening location did not support three clicks after the death wait.");
        using(var hovered=(Bitmap)popup.Clone())
        {
            using(var g=Graphics.FromImage(hovered))g.FillRectangle(Brushes.Gray,profile.Confirm.Button!.Bounds);
            Require(profile.Find(hovered,default)==null&&profile.CanConfirm(hovered,found!,default),"Pre-click check did not use the stable dialog text after hover.");
            using(var g=Graphics.FromImage(hovered))g.FillRectangle(Brushes.Black,profile.Confirm.Marker.Bounds);
            Require(!profile.CanConfirm(hovered,found!,default),"Removed dialog text still authorized confirmation.");
        }
        using(var moved=new Bitmap(popup.Width,popup.Height))
        {
            using(var g=Graphics.FromImage(moved))g.DrawImageUnscaled(popup,60,30);
            Require(profile.Find(moved,default)==null&&!profile.CanConfirm(moved,found!,default),"A moved custom dialog used stale coordinates.");
        }
        using(var resized=new Bitmap(popup,480,320))
            Require(profile.Find(resized,default)==null&&!profile.CanOpen(resized,default),"Wrong window size allowed custom revival input.");
        Require(!profile.CanConfirm(popup,found! with{Point=new(10,10)},default)&&!profile.CanConfirm(popup,found! with{Custom=false},default),
            "Confirmation accepted a mismatched selection or automatic match under a custom profile.");
        Invalid(()=>profile.Validate("other-client",popup.Size),"Another client build accepted the custom profile.");
        Invalid(()=>profile.Validate("fixture-client",new(800,600)),"Another window size accepted the custom profile.");
        Invalid(()=>(profile with{Version=2}).Validate("fixture-client",popup.Size),"Unknown profile version was accepted.");
        Invalid(()=>(profile with{Confirm=profile.Confirm with{X=-1}}).Validate("fixture-client",popup.Size),"Out-of-window click was accepted.");
        Invalid(()=>(profile with{Confirm=profile.Confirm with{Button=null}}).Validate("fixture-client",popup.Size),"Confirmation without button recognition was accepted.");
        Invalid(()=>(profile with{Confirm=profile.Confirm with{Marker=profile.Confirm.Button!}}).Validate("fixture-client",popup.Size),"Overlapping marker and button were accepted.");
        Invalid(()=>(profile with{Opening=profile.Opening! with{X=5000}}).Validate("fixture-client",popup.Size),"Invalid opening click was accepted.");
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();bool stopped=false;
        try{profile.Find(popup,cancellation.Token);}catch(OperationCanceledException){stopped=true;}
        Require(stopped,"Cancelled custom recognition continued.");
        string file=Path.Combine(Path.GetTempPath(),"PoteHunter-revival-"+Guid.NewGuid().ToString("N")+".json");string? backup=null;
        try
        {
            Require(RevivalProfile.Load("fixture-client",popup.Size,file)==null,"Missing profile did not preserve automatic mode.");
            profile.Save(file);byte[] original=File.ReadAllBytes(file);
            var loaded=RevivalProfile.Load("fixture-client",popup.Size,file)!;
            Require(loaded.Find(popup,default)?.Point==found!.Point&&loaded.CanOpen(before,default),"Saved revival setup did not round-trip.");
            Invalid(()=>(profile with{Confirm=profile.Confirm with{X=-1}}).Save(file),"Invalid save overwrote the profile.");
            Require(original.SequenceEqual(File.ReadAllBytes(file)),"A rejected save changed the existing profile.");
            backup=RevivalProfile.UseAutomatic(file);
            Require(!File.Exists(file)&&backup!=null&&original.SequenceEqual(File.ReadAllBytes(backup)),"Use automatic did not preserve a custom-profile backup.");
            File.WriteAllText(file,"{");Invalid(()=>RevivalProfile.Load("fixture-client",popup.Size,file),"Malformed profile silently fell back to automatic clicks.");
            File.WriteAllText(file,"null");Invalid(()=>RevivalProfile.Load("fixture-client",popup.Size,file),"Empty profile was accepted.");
            File.WriteAllText(file,new string(' ',2_000_001));Invalid(()=>RevivalProfile.Load("fixture-client",popup.Size,file),"Oversized profile was accepted.");
        }
        finally{File.Delete(file);File.Delete(file+".tmp");if(backup!=null)File.Delete(backup);}
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"revival-setup-checks.json"),JsonSerializer.Serialize(new
        {
            Passed=true,HardwareInputEmitted=false,SyntheticFixtures=true,
            Checks=new[]{"paired custom dialog and button","optional opening step","custom open/confirm/alive sequence","hover uses independent marker","moved/missing dialogs rejected","client/window binding","invalid/overlapping click areas rejected","cancellation","profile persistence","invalid save preserves prior setup","automatic reset backs up custom setup","malformed/oversized setup stops"}
        },new JsonSerializerOptions{WriteIndented=true}));
    }
}
