namespace PoteHunter;

internal static class GamekeeperReturnPolicy
{
    public static bool Stationary(Options options) => !options.GroupMode && !options.Ranged &&
        !options.HealerMode && Targeting.IsStationaryHuntFilter(options.Target);
    public static bool Required(Options options) => !options.GroupMode &&
        (options.ReturnToHuntLocationAfterGamekeeper || Stationary(options));

    public static bool Ready(bool priorityPresent,bool pending,bool defeated) =>
        !priorityPresent && pending && defeated;

    public static async Task Checks()
    {
        void Require(bool condition,string message)
        {if(!condition)throw new InvalidOperationException("Gamekeeper return: "+message);}
        foreach(string target in new[]{"Mimic","Pulkhan","Tribal","Tower"})
        {
            var options=new Options{Target=target,ReturnToHuntLocationAfterGamekeeper=false};
            Require(Required(options),"stationary filter with legacy return disabled abandoned its anchor");
            options.Ranged=true;Require(!Required(options),"ranged optional return was overridden");
            options.Ranged=false;options.HealerMode=true;Require(!Required(options),"healer optional return was overridden");
            options.HealerMode=false;options.GroupMode=true;Require(!Required(options),"group mode acquired a solo anchor");
        }
        var ordinary=new Options{Target="Green",ReturnToHuntLocationAfterGamekeeper=false};
        Require(!Required(ordinary),"ordinary solo return preference was overridden");
        ordinary.ReturnToHuntLocationAfterGamekeeper=true;Require(Required(ordinary),"enabled optional return was ignored");
        ordinary.GroupMode=true;Require(!Required(ordinary),"group follow was replaced by optional solo return");
        Require(!Ready(false,true,false)&&!Ready(true,true,true)&&!Ready(false,false,true),
            "living, new priority or absent request started a return");
        Require(Ready(false,true,true),"confirmed Gamekeeper death did not own the next transition");

        // A long but steadily progressing excursion must not inherit the short
        // settling timeout. A stationary/nonprogressing return must still end.
        Vec slowPosition=new(80,0);long slowClock=0;
        bool slowReturn=await AnchorArrival.ReturnAsync(()=>slowPosition,default,.5,
            _=>{slowClock+=200;slowPosition-=new Vec(.3,0);return Task.CompletedTask;},()=>{},
            _=>Task.CompletedTask,(ms,ct)=>{slowClock+=ms;return Task.CompletedTask;},()=>slowClock,default,
            extendOnProgress:true);
        Require(slowReturn&&slowClock>15000&&slowClock<120000,"steady distant return timed out at fifteen seconds");
        slowPosition=new(80,0);slowClock=0;
        bool stalledReturn=await AnchorArrival.ReturnAsync(()=>slowPosition,default,.5,
            _=>Task.CompletedTask,()=>{},_=>Task.CompletedTask,
            (ms,ct)=>{slowClock+=ms;return Task.CompletedTask;},()=>slowClock,default,extendOnProgress:true);
        Require(!stalledReturn&&slowClock<=15020,"nonprogressing distant return renewed its deadline");
        slowPosition=default;slowClock=0;
        bool slowFacing=await AnchorArrival.ReturnAsync(()=>slowPosition,default,.5,
            _=>throw new Exception("Settled anchor moved during facing"),()=>{},
            _=>{slowClock+=19000;return Task.CompletedTask;},
            (ms,ct)=>{slowClock+=ms;return Task.CompletedTask;},()=>slowClock,default,extendOnProgress:true);
        Require(slowFacing&&slowClock>15000&&slowClock<120000,"successful bounded saved-facing progress was rejected by the travel deadline");

        // Replay the handoff with a surviving engaged enemy beyond the normal
        // leash. It may be selected only after arrival and saved facing settle.
        foreach(double excursion in new[]{12.231,15.722,23.647})
        {
            Vec anchor=new(20,30),position=anchor+new Vec(excursion,0);long clock=0;
            bool pending=true,defeated=true,facing=false;int selected=0,defended=0;
            var engaged=new HashSet<uint>{123};
            bool returned=await AnchorArrival.ReturnAsync(()=>position,anchor,NearbyLootPickup.AnchorArrivalTolerance,
                _=>{Require(pending&&defeated&&selected==0,"ordinary selection ran during return");
                    position=anchor+new Vec(Math.Max(0,(position-anchor).Length-2),0);return Task.CompletedTask;},
                ()=>{},_=>{facing=true;return Task.CompletedTask;},
                (ms,ct)=>{ct.ThrowIfCancellationRequested();clock+=ms;return Task.CompletedTask;},()=>clock,default,
                defend:_=>{if((position-anchor).Length<=1.5&&defended++==0){clock+=1000;return Task.FromResult(true);}return Task.FromResult(false);});
            Require(returned&&facing&&(position-anchor).Length<=.5&&engaged.Contains(123),
                "return failed arrival/facing or discarded the surviving encounter");
            pending=false;defeated=false;selected++;
            Require(!Ready(false,pending,defeated)&&selected==1,"completed return did not release normal target selection");
        }
    }
}
