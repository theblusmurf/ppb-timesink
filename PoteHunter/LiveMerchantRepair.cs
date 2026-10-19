namespace PoteHunter;

internal sealed class LiveMerchantRepair(World world,MerchantRepairUi setup,Action validate) : IMerchantRepairSurface
{
    readonly HashSet<MerchantRepairAction> attempted=[];
    DurabilityReading? baseline;
    MerchantRepairObservation? previous;
    public long Now=>Environment.TickCount64;
    static bool Marker(Bitmap image,RecognitionSelection selection)=>selection.Marker.MatchesText(image)||selection.Marker.MatchesControl(image);
    static bool Control(Bitmap image,RecognitionSelection selection)=>Marker(image,selection) &&
        (selection.Button==null || selection.Button.MatchesText(image)||selection.Button.MatchesControl(image)||selection.Button.InspectIcon(image).Matched);
    internal static MerchantRepairObservation Recognize(Bitmap image,MerchantRepairUi setup)
    {
        setup.Validate(image.Size);
        bool shop=Marker(image,setup.RepairAll)&&Marker(image,setup.CloseShop);
        bool prompt=setup.Confirm!=null&&Marker(image,setup.Confirm);
        return new(Control(image,setup.Hoys),Control(image,setup.GeneralStore),shop,
            shop&&Control(image,setup.RepairAll),shop&&Control(image,setup.CloseShop),prompt,
            prompt&&Control(image,setup.Confirm!));
    }
    public Task<MerchantRepairObservation> Observe(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();validate();
        using var image=RepairScreen.Capture(world);
        var state=Recognize(image,setup);
        if(baseline!=null && attempted.Contains(MerchantRepairAction.RepairAll))
            state=state with {Repaired=MerchantRepairProfile.Repaired(baseline,world.ReadDurability())};
        validate();token.ThrowIfCancellationRequested();
        if(previous!=state){TraceLog.Record("merchant repair recognition changed",new{Merchant="Hoys",State=state});previous=state;}
        return Task.FromResult(state);
    }
    public async Task Perform(MerchantRepairAction action,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();validate();
        if(!attempted.Add(action))throw new InvalidOperationException("Merchant input was already attempted; refusing a duplicate.");
        var state=await Observe(token);
        bool allowed=action switch
        {
            MerchantRepairAction.Hoys=>state.Hoys&&!state.GeneralStore&&!state.Shop&&!state.Prompt,
            MerchantRepairAction.GeneralStore=>state.GeneralStore&&!state.Shop&&!state.Prompt,
            MerchantRepairAction.RepairAll=>state.Shop&&state.RepairAll&&state.CloseShop&&!state.Prompt,
            MerchantRepairAction.Confirm=>state.Prompt&&state.Confirm,
            MerchantRepairAction.CloseShop=>state.Shop&&state.CloseShop&&!state.Prompt&&state.Repaired,
            _=>false
        };
        if(!allowed)throw new InvalidOperationException("The expected merchant control changed before clicking: "+action);
        var selection=action switch
        {
            MerchantRepairAction.Hoys=>setup.Hoys,MerchantRepairAction.GeneralStore=>setup.GeneralStore,
            MerchantRepairAction.RepairAll=>setup.RepairAll,MerchantRepairAction.CloseShop=>setup.CloseShop,
            _=>setup.Confirm??throw new InvalidOperationException("No merchant confirmation was captured.")
        };
        if(action==MerchantRepairAction.RepairAll)
        {
            baseline=world.ReadDurability();
            if(!baseline.TryValidate(out _,out var reason))throw new InvalidOperationException("Repair All blocked: "+reason);
            if(baseline.Items.All(item=>item.Current==item.Maximum))
                throw new InvalidOperationException("Equipment is already fully repaired. Repair All was not clicked.");
        }
        Rectangle bounds=RepairScreen.Bounds(world);
        Point screen=new(bounds.X+selection.Click.X,bounds.Y+selection.Click.Y);
        Input.MovePointer(screen,token);await Input.Delay(80,token);
        using(var image=RepairScreen.Capture(world))
            if(!Marker(image,selection))throw new InvalidOperationException("The merchant recognition marker changed under the pointer. No click was sent.");
        validate();token.ThrowIfCancellationRequested();
        if(RepairScreen.Bounds(world)!=bounds || !Input.AlignPointer(screen,token,"merchant repair pointer alignment"))
            throw new InvalidOperationException("The merchant window or pointer changed. No click was sent.");
        validate();token.ThrowIfCancellationRequested();
        if(RepairScreen.Bounds(world)!=bounds)throw new InvalidOperationException("The merchant window moved before clicking.");
        await Input.Click(false,token);
        TraceLog.Record("merchant repair control clicked",new{Merchant="Hoys",Action=action.ToString(),HardwareInputSent=true,ResultVerified=false});
    }
    public Task Delay(CancellationToken token)=>Input.Delay(150,token);
}
