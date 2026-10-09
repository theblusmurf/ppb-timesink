using System.ComponentModel;
using System.Text.Json;

namespace PoteHunter;

internal static class CoreReadPerformanceChecks
{
    public static void Run()
    {
        var checks=new List<string>();
        void Check(bool value,string label) { if(!value)throw new Exception("Core read performance: "+label);checks.Add(label); }
        int broad=0;long tick=10;string identity="process/zone/body/generation-1";
        var source=new Dictionary<uint,Health>{{7,new(100,100)}};
        var pass=new ControlHealthPass(()=>identity,()=>{broad++;return source;},()=>tick);
        var first=pass.Read();source[7]=new(90,100);
        Check(pass.Read()[7].Current==100 && broad==1 && ReferenceEquals(first,pass.Read()),
            "three broad observations in one synchronous pass use one immutable read rather than three");
        tick+=ControlHealthPass.MaximumAgeMilliseconds;
        Check(pass.Read()[7].Current==90 && broad==2,"100-ms age boundary requires a new broad observation");
        identity="different-process/zone/body/generation";
        Check(pass.Read()[7].Current==90 && broad==3,"changed context cannot reuse broad health");
        var changing=new ControlHealthPass(()=>identity,()=>{identity="changed mid-read";return source;},()=>tick);
        bool contextRejected=false;try{changing.Read();}catch(InvalidOperationException){contextRejected=true;}
        Check(contextRejected,"process/zone/body context changing during broad read is rejected");
        int delayed=0;
        var slow=new ControlHealthPass(()=>identity,()=>{delayed++;tick+=150;return source;},()=>tick);
        Check(slow.Read()[7].Known && slow.Read()[7].Known && delayed==2,
            "slow broad reads bypass reuse without adding a new timeout-based hunt stop");
        var living=new Health(100,100);

        var keeper=new Entity(0x210000,0x80001752,"Gamekeeper",new(1,0),0,Generation:4,Model:"MON_SnowGun2.GCMDS");
        var ordinary=new Entity(0x220000,0x80000001,"Mimic",new(1,0),0,Generation:2,Model:"MON_mimic.GCMDS");
        int priorityReads=0;
        Health ReadPriority(uint id){priorityReads++;return id==keeper.Id?living:default;}
        Check(PriorityHealthRead.Read([ordinary],ReadPriority).Count==0 && priorityReads==0,
            "ordinary scene with no Gamekeeper performs zero priority HP reads");
        var priority=PriorityHealthRead.Read([ordinary,keeper,keeper with{Position=new(2,0)}],ReadPriority);
        Check(priorityReads==1 && GamekeeperPriority.Choose([keeper],priority,new(0,0),new(0,0),10,10,true)==keeper,
            "multiple observations of one Gamekeeper read its current HP once instead of traversing all UID health");
        int beforeAmbiguous=priorityReads;
        var ambiguous=PriorityHealthRead.Read([keeper,keeper with{Generation=5,Address=0x230000}],ReadPriority);
        Check(priorityReads==beforeAmbiguous && GamekeeperPriority.Choose([keeper],ambiguous,new(0,0),new(0,0),10,10,true)==null,
            "ambiguous pooled Gamekeeper identities cannot borrow HP or a lock");
        foreach(var hp in new[]{default(Health),new Health(0,100)})
            Check(GamekeeperPriority.Choose([keeper],PriorityHealthRead.Read([keeper],_=>hp),new(0,0),new(0,0),10,10,true)==null,
                "unknown/dead fresh priority HP rejected");

        PlayerNames(Check);
        var (full,reused)=Durability(Check);
        var report=new {Passed=true,HardwareInputEmitted=false,GameInputSent=false,LiveGameplayVerified=false,
            Workload="Synthetic synchronous health requests and sixteen occupied equipment slots; not runtime CPU/latency measurements",
            GamekeeperHpReadsWithoutCandidates=0,SynchronousBroadReadsBefore=3,SynchronousBroadReadsAfter=1,
            FullEquipmentFieldReads=full,ReusedEquipmentFieldReads=reused,Checks=checks};
        if(!DiagnosticIo.TryAtomicWrite("write core read performance checks",Path.Combine(AppContext.BaseDirectory,"core-read-performance-checks.json"),
            JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true})))throw new IOException("Could not preserve core read performance check report.");
    }

    static void PlayerNames(Action<bool,string> check)
    {
        var player=new Entity(0x210000,123,"",new(1,2),0,Generation:7,Model:"PC_MAN.GCMDS");
        var names=new Dictionary<uint,string>{{player.Id,"Observed player"}};
        var cache=new PlayerNameDisplayCache();cache.Store("process/scene/actor/uid/zone",[player],new(names,true,"verified"),10);
        names[player.Id]="Mutable caller overwrite";
        List<Entity> Copy(Entity? body=null)=>[body??player];
        var body=Copy();check(cache.Apply("process/scene/actor/uid/zone",body,999,100)==1 && body[0].VerifiedPlayerName=="Observed player" &&
            body[0].Address==player.Address && body[0].Generation==player.Generation && body[0].Name==player.Name,
            "recent display names retain actual overlay text without mutating body/identity or retaining caller dictionaries");
        foreach(var changed in new[]{player with{Address=0x220000},player with{Generation=8},player with{Id=124},player with{Model="changed model"},player with{Name="changed body label"}})
            check(cache.Apply("process/scene/actor/uid/zone",Copy(changed),999,100)==0,"new body/address/generation/model/ID cannot borrow a displayed player name");
        check(cache.Apply("process/scene/actor/uid/zone",Copy(),999,10+PlayerNameDisplayCache.MaximumAgeMilliseconds)==0,
            "display-name cache expires at the exact one-second boundary");
        cache.Store("original-context",[player],new(names,true,"verified"),20);
        check(cache.Apply("different-process-or-zone",Copy(),999,30)==0 && cache.Apply("original-context",Copy(),999,40)==0,
            "changed pointer/process/zone context discards display names even if old context later returns");
        cache.Store("context",[player],new(names,false,"unknown"),20);
        check(cache.Apply("context",Copy(),999,30)==0,"failed enrichment cannot seed a name cache");
        cache.Store("context",[player],new(names,true,"verified"),20);
        check(cache.Apply("context",Copy(),player.Id,30)==0,"local player remains excluded from display enrichment");
    }
    static (int Full,int Reused) Durability(Action<bool,string> check)
    {
        const uint scene=0x100000,equipmentClass=0x849000;const int offset=0x14cc;
        var context=new DurabilityReadContext("client",71,0x400000,scene,0x110000,3,new Entity(0x120000,123,"Synthetic",new(1,2),0,Generation:7));
        var memory=new Dictionary<long,byte[]>{{scene+offset,new byte[64]}};
        uint Wrapper(int slot)=>0x200000u+(uint)slot*0x1000;
        uint Item(int slot)=>Wrapper(slot)+0x400;
        for(int slot=0;slot<16;slot++)
        {
            BitConverter.TryWriteBytes(memory[scene+offset].AsSpan(slot*4),Wrapper(slot));
            var wrapper=new byte[0x1c4];BitConverter.TryWriteBytes(wrapper.AsSpan(0x1c0),Item(slot));memory[Wrapper(slot)]=wrapper;
            var value=new byte[0x19];BitConverter.TryWriteBytes(value.AsSpan(),equipmentClass);
            BitConverter.TryWriteBytes(value.AsSpan(4),Wrapper(slot)+0x800);BitConverter.TryWriteBytes(value.AsSpan(8),(ulong)slot+500);
            BitConverter.TryWriteBytes(value.AsSpan(0x10),(ushort)(slot+100));BitConverter.TryWriteBytes(value.AsSpan(0x12),(ushort)(slot<<4|1));
            value[0x15]=90;value[0x18]=100;memory[Item(slot)]=value;
            var definition=new byte[0x2c];BitConverter.TryWriteBytes(definition.AsSpan(),(ushort)(slot+100));
            BitConverter.TryWriteBytes(definition.AsSpan(0x28),1u);memory[Wrapper(slot)+0x800]=definition;
        }
        int reads=0;long now=0;
        byte[] Read(long address,int count){reads++;return memory.TryGetValue(address,out var value)&&value.Length==count?value.ToArray():throw new Win32Exception(299,"Fixture unreadable");}
        var cache=new DurabilityScreeningCache();DurabilityReading Screen()=>cache.Read(offset,equipmentClass,()=>context,Read,()=>now);
        check(Screen().LowestPercent==90,"full screening retains all sixteen equipped slots");int full=reads;
        reads=0;now=100;check(Screen().LowestPercent==90,"unchanged identity-bound screening retains exact durability");int reused=reads;
        check(full==100 && reused==49 && cache.FullReads==1 && cache.ReusedReads==1,
            "stable sixteen-slot screening reduces actual equipment native field calls from100 to49 with every unique field revalidated");
        memory[Item(15)][0x15]=10;now=200;
        check(Screen().LowestPercent==10 && cache.FullReads==2,"wear in slot16 invalidates screen immediately and performs full fresh read");
        memory[Item(15)][0x18]=0;now=300;
        check(!Screen().Known,"unknown or invalid equipment cannot reuse old valid screening");
        memory[Item(15)][0x18]=100;now=400;Screen();
        context=context with{Zone=8};now=500;long before=cache.FullReads;
        check(Screen().Context==context.Key && cache.FullReads==before+1,"zone/process/body context changes force fresh screening");
        before=cache.FullReads;now+=DurabilityScreeningCache.MaximumAgeMilliseconds;
        check(Screen().Known && cache.FullReads==before+1,"one-second sample expiry requires two-pass reread");
        memory.Remove(Wrapper(3));now++;
        check(!Screen().Known,"unreadable occupied slot fails closed rather than becoming healthy or absent");
        return(full,reused);
    }
}
