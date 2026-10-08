namespace PoteHunter;

// Calibration only observes the game. The departure is established when its
// destination image is captured, after the user has walked to the portal.
internal sealed record TeleporterSetupSample(Entity Body,int Zone,Health Health);

internal static class TeleporterSetupPolicy
{
    internal static void RequireLiving(Entity body,Health health)
    {
        if(body.Id==0||string.IsNullOrWhiteSpace(body.Name)||!body.Position.Finite||!double.IsFinite(body.Height)||!health.Known||health.Dead)
            throw new InvalidOperationException("Teleporter setup requires the same living character with readable HP and a finite live position.");
    }

    internal static bool SameIdentity(Entity original,Entity current)=>
        original.Id==current.Id&&original.Name==current.Name&&original.Model==current.Model;

    static bool SameBody(Entity original,Entity current)=>
        SameIdentity(original,current)&&original.Address==current.Address&&original.Generation==current.Generation;

    internal static void RequireBeforeDeparture(Entity original,TeleporterSetupSample current)
    {
        RequireLiving(current.Body,current.Health);
        if(!SameIdentity(original,current.Body))
            throw new InvalidOperationException("Character changed before the departure capture. Keep the same living character throughout setup.");
        if(current.Zone is not (>=1 and <=18) && current.Zone!=100)
            throw new InvalidOperationException("The current map zone is not ready for teleporter setup.");
    }

    internal static void RequireCaptureStable(Entity original,TeleporterSetupSample before,TeleporterSetupSample after)
    {
        RequireBeforeDeparture(original,before);RequireBeforeDeparture(original,after);
        if(before.Zone!=after.Zone||!SameBody(before.Body,after.Body))
            throw new InvalidOperationException("Character body or map changed during image capture. Wait for the game to settle, then configure again.");
        if((after.Body.Position-before.Body.Position).Length>.5||Math.Abs(after.Body.Height-before.Body.Height)>.5)
            throw new InvalidOperationException("The character is still moving during capture. Remain still at the portal and configure again.");
    }

    internal static void RequireDeparture(TeleporterSetupSample captured,TeleporterSetupSample current)
    {
        RequireBeforeDeparture(captured.Body,captured);
        RequireBeforeDeparture(captured.Body,current);
        if(current.Zone!=captured.Zone||!SameBody(captured.Body,current.Body))
            throw new InvalidOperationException("Character body or map changed after the departure capture. Capture the destination and confirmation at the same portal.");
        if((current.Body.Position-captured.Body.Position).Length>3||Math.Abs(current.Body.Height-captured.Body.Height)>3)
            throw new InvalidOperationException("Remain at the captured departure point until the destination and confirmation are captured.");
    }

    internal static void RequireLanding(TeleporterSetupSample captured,TeleporterSetupSample current)
    {
        RequireBeforeDeparture(captured.Body,captured);
        RequireBeforeDeparture(captured.Body,current);
        if(current.Zone!=captured.Zone)
            throw new InvalidOperationException("The landing must belong to the same living character and map as the captured departure.");
    }
}
