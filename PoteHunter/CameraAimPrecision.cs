using System.Numerics;

namespace PoteHunter;

public readonly record struct CameraAimPrecisionResult(double ScreenErrorPixels,double PickRadiusPixels,bool Aligned);

public static class CameraAimPrecision
{
    const double SafetyFraction=.65;

    public static CameraAimPrecisionResult Evaluate(CameraFrame camera,Vector3 target,int viewportWidth,int viewportHeight)
    {
        if(viewportWidth<=0)throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        if(viewportHeight<=0)throw new ArgumentOutOfRangeException(nameof(viewportHeight));
        if(!float.IsFinite(target.X)||!float.IsFinite(target.Y)||!float.IsFinite(target.Z))
            throw new ArgumentOutOfRangeException(nameof(target));

        ScreenProjection projection=Aim3D.Project(camera,target);
        double eyeDistance=Vector3.Distance(camera.Position,target);
        if(!double.IsFinite(eyeDistance))throw new ArgumentOutOfRangeException(nameof(target));

        // SceneManager::PickingCharacter uses 50 screen pixels at the eye and
        // shrinks that radius linearly to zero at 10,000 client units. World
        // camera/entity coordinates are divided by 100, so the ceiling is 100.
        double pickRadius=Math.Max(0,50*(1-eyeDistance/100));
        if(!projection.Finite||projection.Depth<=0||!projection.IsVisible||pickRadius<=0)
            return new(double.PositiveInfinity,pickRadius,false);

        double xPixels=projection.X*viewportWidth*.5;
        double yPixels=projection.Y*viewportHeight*.5;
        double radial=Math.Sqrt(xPixels*xPixels+yPixels*yPixels);
        return new(radial,pickRadius,radial<=pickRadius*SafetyFraction);
    }
}
