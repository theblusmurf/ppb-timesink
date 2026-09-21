using System.Numerics;

namespace PoteHunter;

public static class CameraAimPrecisionChecks
{
    public static void RunAll()
    {
        const int width=5120,height=1440;
        float horizontalFov=MathF.PI/3;
        float verticalFov=2*MathF.Atan(MathF.Tan(horizontalFov/2)*(float)height/width);
        var camera=new CameraFrame(Vector3.Zero,Vector3.UnitZ,Vector3.UnitY,verticalFov,(float)width/height);
        const float distance=32;
        static Vector3 At(float yaw,float pitch,float distance)=>new(
            distance*MathF.Tan(yaw),distance*MathF.Tan(pitch),distance);
        static void Require(bool condition,string message){if(!condition)throw new Exception(message);}

        var oneAxis=CameraAimPrecision.Evaluate(camera,At(.01f,0,distance),width,height);
        var corner=CameraAimPrecision.Evaluate(camera,At(.01f,.01f,distance),width,height);
        Require(!oneAxis.Aligned&&oneAxis.ScreenErrorPixels>oneAxis.PickRadiusPixels,
            "A .01-radian ultrawide one-axis error passed the source picker radius.");
        Require(!corner.Aligned&&corner.ScreenErrorPixels>oneAxis.ScreenErrorPixels,
            "A .01-radian two-axis corner error passed radial selection precision.");

        var centered=CameraAimPrecision.Evaluate(camera,new Vector3(0,0,distance),width,height);
        var small3D=CameraAimPrecision.Evaluate(camera,At(.003f,.003f,distance),width,height);
        Require(centered.Aligned&&centered.ScreenErrorPixels<.001&&small3D.Aligned,
            "A centered or reasonably small three-dimensional error was rejected.");

        var highResolution=CameraAimPrecision.Evaluate(camera,At(.006f,0,distance),width,height);
        float narrowVertical=2*MathF.Atan(MathF.Tan(horizontalFov/2)*(float)height/(width/2));
        var narrowCamera=camera with {VerticalFovRadians=narrowVertical,AspectRatio=(float)(width/2)/height};
        var lowResolution=CameraAimPrecision.Evaluate(narrowCamera,At(.006f,0,distance),width/2,height);
        Require(!highResolution.Aligned&&lowResolution.Aligned&&
            highResolution.ScreenErrorPixels>lowResolution.ScreenErrorPixels*1.9,
            "Screen-space precision did not scale with viewport resolution.");

        var behind=CameraAimPrecision.Evaluate(camera,new Vector3(0,0,-10),width,height);
        var ceiling=CameraAimPrecision.Evaluate(camera,new Vector3(0,0,100),width,height);
        Require(!behind.Aligned&&double.IsPositiveInfinity(behind.ScreenErrorPixels),
            "A behind-camera target was accepted.");
        Require(!ceiling.Aligned&&ceiling.PickRadiusPixels==0,
            "A target at the source picker's distance ceiling was accepted or given a negative radius.");
    }
}
