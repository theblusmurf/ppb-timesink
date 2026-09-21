namespace PoteHunter;

/// <summary>Deterministic close-target camera-orbit checks; performs no input or process access.</summary>
public static class AimResponsivenessChecks
{
    public static void RunAll()
    {
        const double sensitivity=.0037035344210246063,boom=10,targetDistance=1.8,offset=.2,tolerance=.01;
        double multiplier=Movement.CameraCorrectionMultiplier(boom,targetDistance,true);
        if(Math.Abs(multiplier-6.55555555555556)>1e-12)throw new InvalidOperationException("Camera parallax multiplier changed unexpectedly.");
        if(Movement.CameraCorrectionMultiplier(boom,targetDistance,false)!=1||Movement.CameraCorrectionMultiplier(boom,.001,true)!=1)
            throw new InvalidOperationException("Invalid pivot geometry did not fall back to optical correction.");

        Result baseline=Simulate(false,false),oldPivot=Simulate(true,false),compensated=Simulate(false,true);
        if(baseline.Error>tolerance||baseline.Iterations!=27)
            throw new InvalidOperationException("Baseline close-target simulation changed unexpectedly.");
        if(oldPivot.Error<=tolerance)
            throw new InvalidOperationException("Offset scenario no longer demonstrates the pivot-only residual.");
        if(compensated.Error>tolerance||compensated.Iterations>=baseline.Iterations||Math.Abs(compensated.FirstPixels)>96)
            throw new InvalidOperationException("Compensated camera controller did not converge quickly and within its pixel bound.");

        Result Simulate(bool pivotOnly,bool compensate)
        {
            double angle=.30;int first=0,iteration;
            for(iteration=0;iteration<120;iteration++)
            {
                double optical=OpticalError(angle);
                if(Math.Abs(optical)<=tolerance)break;
                double error=pivotOnly?-angle:optical*(compensate?Movement.CameraCorrectionMultiplier(boom,targetDistance,true):1);
                double gain=pivotOnly||compensate?Movement.ControlGain(error):.45;
                int pixels=Math.Clamp((int)Math.Round(error/sensitivity*gain),-96,96);
                if(pixels==0)pixels=Math.Sign(error/sensitivity);
                if(iteration==0)first=pixels;
                angle=Wrap(angle+pixels*sensitivity);
            }
            return new(iteration,first,Math.Abs(OpticalError(angle)));
        }

        double OpticalError(double angle)
        {
            double eyeX=-boom*Math.Cos(angle),eyeZ=-boom*Math.Sin(angle)+offset;
            return Wrap(Math.Atan2(-eyeZ,targetDistance-eyeX)-angle);
        }
        static double Wrap(double angle)=>Math.Atan2(Math.Sin(angle),Math.Cos(angle));
    }

    readonly record struct Result(int Iterations,int FirstPixels,double Error);
}
