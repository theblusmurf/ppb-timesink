using System.Numerics;

namespace PoteHunter;

public readonly record struct RangedTargetOcclusion(Entity? Blocker,double EntryDistance,string Reason)
{
    public bool Blocked=>Blocker!=null;
    public static RangedTargetOcclusion Clear=>new(null,double.PositiveInfinity,"No loaded living monster intersects the camera-to-target creature ray.");
}

/// <summary>
/// Conservative creature-only occlusion for ranged cursor targeting. This does
/// not claim terrain, model-mesh, or server line-of-sight visibility.
/// </summary>
public static class RangedTargetVisibility
{
    // Supplied client source builds its generic character cross-billboard from
    // +/-50 horizontal and 0..200 vertical client units. World readers scale
    // those client coordinates by 1/100, yielding this 0.5 x 2-unit envelope.
    public const double CreatureRadius=.5;
    public const double CreatureHeight=2;

    public static RangedTargetOcclusion FindFrontBlocker(in CameraFrame camera,Entity intended,IEnumerable<Entity> loadedLivingMonsters)
    {
        Aim3D.ValidateCamera(camera);
        ArgumentNullException.ThrowIfNull(intended);
        ArgumentNullException.ThrowIfNull(loadedLivingMonsters);
        RequireValid(intended,nameof(intended));
        Vector3 target=AimPoint(intended),ray=target-camera.Position;
        double targetDistance=ray.Length();
        if(!double.IsFinite(targetDistance)||targetDistance<=1e-4)throw new ArgumentException("Intended target is too close to the camera eye.",nameof(intended));
        Vector3 direction=ray/(float)targetDistance;

        Entity? nearest=null;double nearestEntry=double.PositiveInfinity;
        foreach(Entity? blocker in loadedLivingMonsters)
        {
            if(blocker==null||SameIdentity(blocker,intended)||!blocker.Monster||!Valid(blocker))continue;
            double centerDepth=Vector3.Dot(AimPoint(blocker)-camera.Position,direction);
            const double centerMargin=.01;
            if(!double.IsFinite(centerDepth)||centerDepth<=centerMargin||centerDepth>=targetDistance-centerMargin)continue;
            if(!TryEnterBody(camera.Position,direction,targetDistance,blocker,out double entry))continue;
            if(entry<nearestEntry){nearest=blocker;nearestEntry=entry;}
        }
        return nearest==null?RangedTargetOcclusion.Clear:new(nearest,nearestEntry,
            $"Loaded living monster {nearest.DisplayName} intersects the camera ray {nearestEntry:F2} units before the intended target.");
    }

    static bool TryEnterBody(Vector3 eye,Vector3 direction,double targetDistance,Entity blocker,out double entry)
    {
        double ox=eye.X-blocker.Position.X,oz=eye.Z-blocker.Position.Y;
        double a=direction.X*direction.X+direction.Z*direction.Z;
        double radialStart,radialEnd;
        if(a<1e-10)
        {
            if(ox*ox+oz*oz>CreatureRadius*CreatureRadius){entry=0;return false;}
            radialStart=double.NegativeInfinity;radialEnd=double.PositiveInfinity;
        }
        else
        {
            double b=2*(ox*direction.X+oz*direction.Z);
            double c=ox*ox+oz*oz-CreatureRadius*CreatureRadius;
            double discriminant=b*b-4*a*c;
            if(discriminant<0){entry=0;return false;}
            double root=Math.Sqrt(Math.Max(0,discriminant));
            radialStart=(-b-root)/(2*a);radialEnd=(-b+root)/(2*a);
        }

        double bottom=blocker.Height,top=bottom+CreatureHeight,verticalStart,verticalEnd;
        if(Math.Abs(direction.Y)<1e-10)
        {
            if(eye.Y<bottom||eye.Y>top){entry=0;return false;}
            verticalStart=double.NegativeInfinity;verticalEnd=double.PositiveInfinity;
        }
        else
        {
            double t0=(bottom-eye.Y)/direction.Y,t1=(top-eye.Y)/direction.Y;
            verticalStart=Math.Min(t0,t1);verticalEnd=Math.Max(t0,t1);
        }
        double start=Math.Max(0,Math.Max(radialStart,verticalStart));
        double end=Math.Min(targetDistance,Math.Min(radialEnd,verticalEnd));
        const double endpointMargin=.01;
        entry=start;
        return end>endpointMargin&&start<targetDistance-endpointMargin&&end>=start;
    }

    static Vector3 AimPoint(Entity entity)=>new((float)entity.Position.X,(float)entity.Height+1f,(float)entity.Position.Y);
    static bool SameIdentity(Entity a,Entity b)=>a.Address==b.Address&&a.Id==b.Id&&a.Generation==b.Generation;
    static bool Valid(Entity entity)=>entity.Address>=0x10000&&entity.Position.Finite&&double.IsFinite(entity.Height);
    static void RequireValid(Entity entity,string name)
    {
        if(!Valid(entity))throw new ArgumentException("Entity must have a loaded address and finite world position/height.",name);
    }
}
