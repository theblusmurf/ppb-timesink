using System.Text.Json;

namespace PoteHunter;

internal static class LootReturnFacingChecks
{
    static void Require(bool value,string reason)
    {if(!value)throw new InvalidOperationException("Loot return facing: "+reason);}

    internal static async Task Run()
    {
        var options=new Options{Target="Tower",GroupMode=false,HealerMode=false,Ranged=false,LeaveAreaWhenEmpty=false};
        int turns=0,kept=0,stops=0,validations=0;long now=0;Vec position=new(.805,0);
        Task Delay(int ms,CancellationToken ct){ct.ThrowIfCancellationRequested();now+=ms;return Task.CompletedTask;}
        Task Finish(CancellationToken ct)=>LootReturnFacing.CompleteAsync(options,false,()=>stops++,
            token=>{token.ThrowIfCancellationRequested();validations++;},
            ()=>AnchorArrival.Settled(position,position,default,.5),
            _=>{turns++;throw new Exception("Recorded fixed-melee loot return entered the 6.4-second heading retry.");},
            ()=>kept++,ct);
        bool arrived=await AnchorArrival.ReturnAsync(()=>position,default,.5,
            _=>{position=new(.39,0);return Task.CompletedTask;},()=>stops++,Finish,Delay,()=>now,default);
        Require(arrived&&turns==0&&kept==1&&validations==1&&now==260&&stops>0,
            "settled fixed melee return waited for idle facing or bypassed physical arrival");

        // Returning to the endpoint is still required even though its idle yaw
        // is optional. Motion after the first settle must cause another return.
        now=0;position=default;int approaches=0,finishes=0;
        arrived=await AnchorArrival.ReturnAsync(()=>position,default,.5,
            _=>{approaches++;position=default;return Task.CompletedTask;},()=>{},
            ct=>{if(finishes++==0)position=new(.7,0);return Task.CompletedTask;},Delay,()=>now,default);
        Require(arrived&&approaches==1&&finishes==2,"skipping idle yaw accepted a drifting endpoint");

        foreach(string mode in new[]{"required-facing","unsettled","group","healer","ranged","roaming","other target"})
        {
            var selected=new Options{Target="Tower",GroupMode=false,HealerMode=false,Ranged=false,LeaveAreaWhenEmpty=false};
            if(mode=="group")selected.GroupMode=true;
            if(mode=="healer")selected.HealerMode=true;
            if(mode=="ranged")selected.Ranged=true;
            if(mode=="roaming")selected.LeaveAreaWhenEmpty=true;
            if(mode=="other target")selected.Target="Ichman";
            int restored=0,skipped=0;
            await LootReturnFacing.CompleteAsync(selected,mode=="required-facing",()=>{},_=>{},()=>mode!="unsettled",
                _=>{restored++;return Task.CompletedTask;},()=>skipped++,default);
            Require(restored==1&&skipped==0,"mandatory or incompatible context lost facing: "+mode);
        }
        foreach(bool cancel in new[]{false,true})
        {
            bool rejected=false;int skipped=0;
            try
            {
                await LootReturnFacing.CompleteAsync(options,false,()=>{},_=>throw new OperationCanceledException("Focus/context lost"),
                    ()=>true,_=>Task.CompletedTask,()=>skipped++,new CancellationToken(cancel));
            }
            catch(OperationCanceledException){rejected=true;}
            Require(rejected&&skipped==0,"cancelled/unverified return resumed targeting");
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"loot-return-facing-checks.json"),JsonSerializer.Serialize(new
        {Passed=true,HardwareInputEmitted=false,Checks=new[]{"recorded fixed-melee return settles without idle heading turns/retries",
            "drifting endpoint must return again","existing mandatory facing retained","group/healer/ranged/roaming/nonstationary facing retained",
            "focus/context validation and cancellation retained"}},new JsonSerializerOptions{WriteIndented=true}));
    }
}
