using System.Text.Json;
namespace PoteHunter;

internal static class MerchantRepairChecks
{
    sealed class Surface(bool confirmation=false) : IMerchantRepairSurface
    {
        public long Now {get;private set;}
        public readonly List<MerchantRepairAction> Actions=[];
        public string Failure="";
        public int Stage;
        public MerchantRepairObservation? Initial;
        public Task<MerchantRepairObservation> Observe(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if(Initial is {} initial)return Task.FromResult(initial);
            return Task.FromResult(Stage switch
            {
                0=>new MerchantRepairObservation(Hoys:true),
                1=>new(GeneralStore:Failure!="menu"),
                2=>new(Shop:true,RepairAll:Failure!="repair",CloseShop:true),
                3 when confirmation=>new(Shop:true,Prompt:true,Confirm:Failure!="confirm"),
                3 or 4=>new(Shop:true,CloseShop:true,Repaired:Failure!="durability"),
                _=>default
            });
        }
        public Task Perform(MerchantRepairAction action,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();Actions.Add(action);
            Stage=action switch{MerchantRepairAction.Hoys=>1,MerchantRepairAction.GeneralStore=>2,
                MerchantRepairAction.RepairAll=>3,MerchantRepairAction.Confirm=>4,_=>5};
            return Task.CompletedTask;
        }
        public Task Delay(CancellationToken token){token.ThrowIfCancellationRequested();Now+=150;return Task.CompletedTask;}
    }
    internal static async Task Run()
    {
        void Require(bool condition,string reason){if(!condition)throw new Exception(reason);}
        foreach(bool confirmation in new[]{false,true})
        {
            var surface=new Surface(confirmation);await MerchantRepair.Run(surface,confirmation,default);
            var expected=new List<MerchantRepairAction>{MerchantRepairAction.Hoys,MerchantRepairAction.GeneralStore,MerchantRepairAction.RepairAll};
            if(confirmation)expected.Add(MerchantRepairAction.Confirm);expected.Add(MerchantRepairAction.CloseShop);
            Require(surface.Actions.SequenceEqual(expected),"Merchant repair lost its ordered, once-only controls.");
        }
        foreach(string failure in new[]{"menu","repair","confirm","durability"})
        {
            var surface=new Surface(true){Failure=failure};bool stopped=false;
            try{await MerchantRepair.Run(surface,true,default);}catch(InvalidOperationException){stopped=true;}
            Require(stopped&&surface.Actions.Distinct().Count()==surface.Actions.Count&&
                !surface.Actions.Contains(MerchantRepairAction.CloseShop),"An unrecognized or unverified merchant repair was retried or allowed return.");
        }
        foreach(var initial in new[]{new MerchantRepairObservation(),new(Prompt:true),new(Hoys:true,Shop:true),new(Hoys:true,GeneralStore:true)})
        {
            var surface=new Surface{Initial=initial};bool stopped=false;
            try{await MerchantRepair.Run(surface,false,default);}catch(InvalidOperationException){stopped=true;}
            Require(stopped&&surface.Actions.Count==0,"An ambiguous initial merchant screen authorized input.");
        }
        using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel();var surface=new Surface();bool stopped=false;
            try{await MerchantRepair.Run(surface,false,cancel.Token);}catch(OperationCanceledException){stopped=true;}
            Require(stopped&&surface.Actions.Count==0,"Cancelled merchant repair emitted input.");
        }
        var hunt=new SavedNavigationRoute(8,new(10,0),0,[new(10,0),new(5,0),new(0,0)],DateTime.UtcNow,"Fixture",10);
        var merchant=new SavedNavigationRoute(8,new(0,10),1,[new(0,10),new(0,5),new(0,0)],DateTime.UtcNow,"Fixture",10);
        var profile=new MerchantRepairProfile(1,"fixture",merchant,10,null);
        var plan=profile.Plan([hunt],new(1,1,"Fixture",new(10,0),10),8,10);
        Require(plan.Destination==merchant.Anchor&&plan.Points.Contains(new Vec(0,0))&&hunt.Anchor==new Vec(10,0)&&hunt.Points.Length==3,
            "Merchant travel did not pass through the shared origin or mutated the hunt route.");
        bool refused=false;try{profile.Plan([hunt],new(1,1,"Other",new(10,0),10),8,10);}catch(RouteUnavailableException){refused=true;}
        Require(refused,"Another character used a merchant route.");
        refused=false;try{profile.Plan([hunt],new(1,1,"Fixture",new(10,0),10),9,10);}catch(RouteUnavailableException){refused=true;}
        Require(refused,"A merchant trip crossed an unrecorded map.");
        RepairPatch Patch(int x,int y)=>new(x,y,20,10,Enumerable.Range(0,600).Select(i=>(byte)(i%6<3?0:255)).ToArray());
        RecognitionSelection Target(int x)=>new(Patch(x,10),null,new(x+10,15));
        RecognitionSelection Button(int x)=>new(Patch(200,10),Patch(x,40),new(x+10,45));
        var ui=new MerchantRepairUi(600,400,Target(10),Target(100),Button(250),Button(300),null);ui.Validate(new(600,400));
        foreach(var invalid in new[]{ui with{Hoys=null!},ui with{Width=601},ui with{Hoys=ui.Hoys with{Click=new(500,300)}},
            ui with{RepairAll=ui.RepairAll with{Button=ui.RepairAll.Marker}}})
        {
            refused=false;try{invalid.Validate(new(600,400));}catch(InvalidOperationException){refused=true;}
            Require(refused,"An incomplete, moved-window or overlapping merchant setup was accepted.");
        }
        string folder=Path.Combine(Path.GetTempPath(),"PoteHunter-merchant-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        string file=Path.Combine(folder,"merchant-repair-profile.json");
        try
        {
            (profile with{Ui=ui}).Save(file);var loaded=MerchantRepairProfile.Load(file);
            Require(loaded.Route.Points.SequenceEqual(merchant.Points)&&loaded.Ui?.Hoys.Click==ui.Hoys.Click&&loaded.OriginHeight==10,
                "Merchant route or captured control did not round-trip.");
            _=loaded.ControlsFor("FIXTURE",new(600,400));
            refused=false;try{loaded.ControlsFor("changed-client",new(600,400));}catch(InvalidOperationException){refused=true;}
            Require(refused,"A changed game client authorized stale merchant captures.");
        }
        finally{if(File.Exists(file))File.Delete(file);if(File.Exists(file+".tmp"))File.Delete(file+".tmp");Directory.Delete(folder);}
        var time=DateTime.UtcNow;var before=new DurabilityReading(true,"fixture",[new("Weapon","one",10,100)],time.AddSeconds(-1),"");
        var after=before with{ObservedUtc=time,Items=[new("Weapon","one",100,100)]};
        Require(MerchantRepairProfile.Repaired(before,after)&&!MerchantRepairProfile.Repaired(before,before)&&
            !MerchantRepairProfile.Repaired(before,after with{Known=false})&&
            !MerchantRepairProfile.Repaired(before,after with{Items=[new("Weapon","other",100,100)]})&&
            !MerchantRepairProfile.Repaired(before,after with{Items=[new("Weapon","one",90,100)]})&&
            !MerchantRepairProfile.Repaired(before,after with{ObservedUtc=time.AddSeconds(-10)}),"Merchant repair accepted unproved, stale, partial or replaced equipment.");
        var legacy=JsonSerializer.Deserialize<Options>("{\"AutoRepairAfterDeath\":true}")!;
        var saved=JsonSerializer.Deserialize<Options>(JsonSerializer.Serialize(new Options{EquipmentRepairMethod=RepairMethod.Merchant}))!;
        Require(legacy.EquipmentRepairMethod==RepairMethod.Inventory&&saved.EquipmentRepairMethod==RepairMethod.Merchant,
            "Legacy inventory repair or merchant method persistence changed.");
        long clock=0;int commands=0;var observed=new Health(100,100);
        var dead=await RepairThresholdKill.Run(()=>observed,()=>{},ct=>{commands++;return Task.CompletedTask;},
            (ms,ct)=>{clock+=ms;observed=new(0,100);return Task.CompletedTask;},()=>clock,default);
        Require(commands==1&&dead.Dead,"Threshold /kill did not wait for confirmed death after exactly one command.");
        foreach(bool unknown in new[]{false,true})
        {
            clock=0;commands=0;observed=new(100,100);bool stopped=false;
            try{await RepairThresholdKill.Run(()=>observed,()=>{},ct=>{commands++;return Task.CompletedTask;},
                (ms,ct)=>{clock+=ms;if(unknown)observed=default;return Task.CompletedTask;},()=>clock,default);}
            catch(InvalidOperationException){stopped=true;}
            Require(stopped&&commands==1,"Unconfirmed death or unknown health repeated the repair /kill command.");
        }
        var killSettings=JsonSerializer.Deserialize<Options>(JsonSerializer.Serialize(new Options{EquipmentRepairMethod=RepairMethod.Merchant,UseKillForMerchantRepair=true}))!;
        Require(!legacy.UseKillForMerchantRepair&&killSettings.UseKillForMerchantRepair,"Threshold /kill lost its opt-in saved state.");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"merchant-repair-checks.json"),JsonSerializer.Serialize(new{Passed=true,HardwareInputEmitted=false,
            Checks=new[]{"ordered Hoys/menu/shop/repair/optional confirmation/close","no repeated paid action","missing controls and unverified durability block return","cancel and ambiguous screen block input","shared-origin travel preserves hunt slots","character and map restrictions","private route/control persistence and invalid captures rejected","fresh same-equipment full durability required","legacy inventory default and merchant persistence","one threshold command followed by confirmed death; timeout and unknown health cannot repeat it","threshold command remains opt-in"}}));
    }
}
