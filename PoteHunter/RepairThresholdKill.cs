namespace PoteHunter;

internal static class RepairThresholdKill
{
    internal const int TimeoutMilliseconds=15000;
    internal static async Task<Health> Run(Func<Health> health,Action validate,Func<CancellationToken,Task> send,
        Func<int,CancellationToken,Task> delay,Func<long> clock,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();validate();var initial=health();
        if(!initial.Known)throw new InvalidOperationException("Repair /kill blocked: health is unknown.");
        if(initial.Dead)return initial;
        await send(token);long started=clock();
        using var bounded=CancellationTokenSource.CreateLinkedTokenSource(token);bounded.CancelAfter(TimeoutMilliseconds);
        try
        {
            while(clock()-started<TimeoutMilliseconds)
            {
                bounded.Token.ThrowIfCancellationRequested();validate();var observed=health();
                if(!observed.Known)throw new InvalidOperationException("Health became unknown after repair /kill. Stopped without repeating the command.");
                if(observed.Dead)return observed;
                await delay(150,bounded.Token);
            }
        }
        catch(OperationCanceledException) when(!token.IsCancellationRequested&&bounded.IsCancellationRequested){ }
        throw new InvalidOperationException("Repair /kill did not produce confirmed death within 15 seconds. Stopped without another command.");
    }
}

public sealed partial class HunterForm
{
    async Task RequestMerchantRepairDeath(Options options,CancellationToken token)
    {
        if(!options.AutoReviveAfterDeath||!options.AutoRepairAfterDeath||options.GroupMode||options.HealerMode)
            throw new InvalidOperationException("Repair /kill requires solo mode, Auto revive + return, and Auto repair after revival.");
        var setupProblem=MerchantRepairStartProblem(options);
        if(setupProblem!=null)throw new InvalidOperationException(setupProblem);
        var self=world.LocalPlayer();int pid=world.Pid,zone=world.ActiveZone();nint window=world.Window;
        var previousPreflight=Input.Preflight;
        void Validate()
        {
            Input.CheckSafety(token);var current=world.LocalPlayer();
            if(!world.ConnectionVerified||world.Pid!=pid||world.Window!=window||world.ActiveZone()!=zone||!LocalCharacter.Same(self,current))
                throw new InvalidOperationException("Repair /kill stopped because the client, character or map changed.");
        }
        repairInProgress=true;Input.PickupHoldProvider=null;ReleaseCombatPickup();movement?.StopApproach();Input.Release();
        try
        {
            Input.Preflight=()=>
            {
                Validate();var hp=world.TargetHealth(self.Id);
                if(!hp.Known)throw new InvalidOperationException("Repair /kill stopped: health is unknown.");
                if(hp.Dead){ObserveDeath(hp);throw new DeathRecoveryRequiredException();}
            };
            message="Low durability · /kill once, then revival and Hoys repair";
            async Task Send(CancellationToken ct)
            {
                TraceLog.Record("repair threshold command requested",new{Command="/kill",Merchant="Hoys",RepeatAllowed=false});
                await Input.RepairKillCommand(ct);
                TraceLog.Record("repair threshold command sent",new{Command="/kill",DeathVerified=false});
            }
            var dead=await RepairThresholdKill.Run(()=>world.TargetHealth(self.Id),Validate,Send,
                (milliseconds,ct)=>Task.Delay(milliseconds,ct),()=>Environment.TickCount64,token);
            ObserveDeath(dead);TraceLog.Record("repair threshold death confirmed",new{Command="/kill",Merchant="Hoys",DeathVerified=true});
            throw new DeathRecoveryRequiredException();
        }
        catch(DeathRecoveryRequiredException)
        {
            var hp=world.TargetHealth(self.Id);if(hp.Known&&hp.Dead)ObserveDeath(hp);throw;
        }
        finally{Input.Release();Input.Preflight=previousPreflight;repairInProgress=false;}
    }
}
