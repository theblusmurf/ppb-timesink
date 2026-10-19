using System.Text.Json;

namespace PoteHunter;

public enum RepairMethod { Inventory, Merchant }

internal sealed record MerchantRepairUi(int Width,int Height,RecognitionSelection Hoys,
    RecognitionSelection GeneralStore,RecognitionSelection RepairAll,RecognitionSelection CloseShop,RecognitionSelection? Confirm)
{
    internal void Validate(Size size)
    {
        if(size!=new Size(Width,Height))throw new InvalidOperationException("The game window size changed. Capture Hoys repair controls again.");
        if(Hoys==null||GeneralStore==null||RepairAll==null||CloseShop==null)throw new InvalidOperationException("Merchant setup is incomplete.");
        foreach(var (selection,target) in new[]{(Hoys,true),(GeneralStore,true),(RepairAll,false),(CloseShop,false),(Confirm,false)})
        {
            if(selection==null)continue;
            if(selection.Marker==null || !selection.Marker.Valid(size) || !new Rectangle(Point.Empty,size).Contains(selection.Click) ||
                (target ? selection.Button!=null || !selection.Marker.Bounds.Contains(selection.Click) :
                    selection.Button==null || !selection.Button.Valid(size) || !selection.Button.Bounds.Contains(selection.Click) ||
                    selection.Marker.Bounds.IntersectsWith(selection.Button.Bounds)))
                throw new InvalidOperationException("Merchant setup contains an invalid recognition area or click point. Capture it again.");
        }
    }
}

internal sealed record MerchantRepairProfile(int Version,string ClientHash,SavedNavigationRoute Route,double OriginHeight,MerchantRepairUi? Ui)
{
    internal static string PathName=>Path.Combine(AppContext.BaseDirectory,"merchant-repair-profile.json");
    internal void Validate()
    {
        if(Version!=1 || string.IsNullOrWhiteSpace(ClientHash) || Route==null || !RecoveryTravel.Recorded(Route) ||
            string.IsNullOrWhiteSpace(Route.Character) || !double.IsFinite(Route.Heading) || !double.IsFinite(Route.Height) ||
            Route.Height<=0 || !double.IsFinite(OriginHeight) || OriginHeight<=0)
            throw new InvalidOperationException("Hoys needs a complete recorded route from the shared route start. Record it again.");
        Ui?.Validate(new(Ui.Width,Ui.Height));
    }
    internal void Save(string? path=null)
    {
        Validate();string file=path??PathName;
        File.WriteAllText(file+".tmp",JsonSerializer.Serialize(this));File.Move(file+".tmp",file,true);
    }
    internal MerchantRepairUi ControlsFor(string clientHash,Size size)
    {
        Validate();
        if(!ClientHash.Equals(clientHash,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The game client changed. Capture Hoys shop controls again before merchant repair.");
        var controls=Ui??throw new InvalidOperationException("Capture Hoys shop controls before merchant repair.");
        controls.Validate(size);return controls;
    }
    internal static MerchantRepairProfile Load(string? path=null)
    {
        string file=path??PathName;
        if(!File.Exists(file))throw new InvalidOperationException("Record the Hoys route and capture his shop controls in Setup > Equipment repair.");
        if(new FileInfo(file).Length>5_000_000)throw new InvalidOperationException("Hoys repair setup is too large or invalid.");
        MerchantRepairProfile profile;
        try{profile=JsonSerializer.Deserialize<MerchantRepairProfile>(File.ReadAllText(file))??throw new InvalidDataException("Empty merchant setup.");}
        catch(JsonException ex){throw new InvalidOperationException("Hoys repair setup could not be read. Capture it again.",ex);}
        profile.Validate();return profile;
    }
    internal SavedTravel Plan(IReadOnlyList<SavedNavigationRoute?> huntRoutes,Entity self,int zone,double corridor)
    {
        Validate();
        if(!RecoveryRouting.CompatibleIdentity(Route,zone,self.Name))
            throw new RouteUnavailableException("The Hoys route belongs to another character or map. Merchant repair needs a same-map recorded route.");
        var compatible=huntRoutes.Where(r=>r!=null && RecoveryTravel.SharedOrigin(r,Route)).Append(Route).ToArray();
        return RecoveryTravel.Plan(compatible,Route,self.Position,false,corridor);
    }
    internal static bool Repaired(DurabilityReading before,DurabilityReading after)
    {
        if(!before.TryValidate(out _,out _) || !after.TryValidate(out _,out _) ||
            !DurabilityRepairPolicy.SameEquipment(before,after) || !before.Items.Any(i=>i.Current<i.Maximum) ||
            after.ObservedUtc<=before.ObservedUtc || DateTime.UtcNow-after.ObservedUtc>TimeSpan.FromSeconds(3) ||
            after.ObservedUtc>DateTime.UtcNow.AddSeconds(1))return false;
        return before.Items.All(item=>after.Items.Single(i=>i.Slot==item.Slot) is {} current &&
            current.Maximum==item.Maximum && current.Current==current.Maximum);
    }
}

internal enum MerchantRepairAction { Hoys, GeneralStore, RepairAll, Confirm, CloseShop }
internal readonly record struct MerchantRepairObservation(bool Hoys=false,bool GeneralStore=false,bool Shop=false,
    bool RepairAll=false,bool CloseShop=false,bool Prompt=false,bool Confirm=false,bool Repaired=false);
internal interface IMerchantRepairSurface
{
    long Now {get;}
    Task<MerchantRepairObservation> Observe(CancellationToken token);
    Task Perform(MerchantRepairAction action,CancellationToken token);
    Task Delay(CancellationToken token);
}
internal static class MerchantRepair
{
    internal static async Task Run(IMerchantRepairSurface surface,bool confirmation,CancellationToken token)
    {
        async Task Wait(Func<MerchantRepairObservation,bool> ready,string reason,bool allowPrompt=false)
        {
            long started=surface.Now;
            using var phase=CancellationTokenSource.CreateLinkedTokenSource(token);phase.CancelAfter(AutoRepair.PhaseTimeoutMilliseconds);
            try
            {
                while(surface.Now-started<AutoRepair.PhaseTimeoutMilliseconds)
                {
                    phase.Token.ThrowIfCancellationRequested();var state=await surface.Observe(phase.Token);
                    if(state.Prompt && !allowPrompt)throw new InvalidOperationException("An unexpected merchant confirmation is open. Stopped without clicking it.");
                    if(ready(state))return;
                    await surface.Delay(phase.Token);
                }
            }
            catch(OperationCanceledException) when(!token.IsCancellationRequested && phase.IsCancellationRequested){ }
            throw new InvalidOperationException(reason+" No paid action will be repeated.");
        }
        var initial=await surface.Observe(token);
        if(!initial.Hoys || initial.GeneralStore || initial.Shop || initial.Prompt)
            throw new InvalidOperationException("Hoys' captured name is not visible, or a menu is already open. Merchant repair stopped before clicking.");
        await surface.Perform(MerchantRepairAction.Hoys,token);
        await Wait(s=>s.GeneralStore&&!s.Shop,"General Store Merchant was not recognized.");
        await surface.Perform(MerchantRepairAction.GeneralStore,token);
        await Wait(s=>s.Shop&&s.RepairAll&&s.CloseShop,"Hoys' shop and Repair All controls were not recognized.");
        await surface.Perform(MerchantRepairAction.RepairAll,token);
        if(confirmation)
        {
            await Wait(s=>s.Prompt&&s.Confirm,"The captured merchant repair confirmation was not recognized.",true);
            await surface.Perform(MerchantRepairAction.Confirm,token);
        }
        await Wait(s=>s.Repaired&&!s.Prompt,"Repair All did not restore the same equipped items to full durability.",confirmation);
        await Wait(s=>s.Shop&&s.CloseShop&&!s.Prompt,"The shop close control was not recognized.");
        await surface.Perform(MerchantRepairAction.CloseShop,token);
        await Wait(s=>!s.Shop&&!s.GeneralStore&&!s.Prompt,"Hoys' shop did not close; route return was paused.");
    }
}
