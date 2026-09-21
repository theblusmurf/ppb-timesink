namespace PoteHunter;

/// <summary>Pure controller checks for smoothing memory-derived 3D aim errors.</summary>
public static class AimSmoothingChecks
{
    public static void RunAll()
    {
        static void Expect(bool value,string name)
        {
            if(!value)throw new InvalidOperationException("Aim smoothing check failed: "+name);
        }

        double smooth=Movement.SmoothAimAxis(.10,.06);
        Expect(smooth is >.06 and <.10,"same-direction correction is smoothed");
        Expect(Movement.SmoothAimAxis(.02,-.02)==-.02,"direction reversal stays immediate");
        Expect(Movement.SmoothAimAxis(.02,.01)==.01,"fine picker correction stays exact");
        Expect(Movement.SmoothAimAxis(.02,.20)==.20,"large turn stays immediate");
        Expect(Movement.SmoothAimAxis(double.NaN,.04)==.04,"invalid history cannot poison current memory aim");
        Expect(Movement.VerticalControlGain(.20)>.9&&
            Movement.VerticalControlGain(.08)>.8&&
            Movement.VerticalControlGain(.01)>.69,"vertical controller retains aggressive coarse and fine gains");
    }
}
