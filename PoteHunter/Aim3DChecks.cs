using System.Numerics;

namespace PoteHunter;

/// <summary>Deterministic geometry checks for Aim3D. No game state, input, or fabricated camera offsets.</summary>
public static class Aim3DChecks
{
    public static void RunAll()
    {
        var camera = new CameraFrame(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, MathF.PI / 2, 16f / 9f);
        var p = new AimParameters(8, 0.002f, 0.5f);
        Expect(Aim3D.Project(camera, new Vector3(0, 0, 5)).X == 0, "center projection");
        var right = Aim3D.ComputeStep(camera, new Vector3(2, 0, 5), .01f, .01f, p);
        Expect(right.YawErrorRadians > 0 && right.Pixels.X > 0, "right target signed yaw");
        var left = Aim3D.ComputeStep(camera, new Vector3(-2, 0, 5), .01f, .01f, p);
        Expect(left.YawErrorRadians < 0 && left.Pixels.X < 0, "left target signed yaw");
        var above = Aim3D.ComputeStep(camera, new Vector3(0, 2, 5), .01f, .01f, p);
        Expect(above.PitchErrorRadians > 0 && above.Pixels.Y > 0, "above target signed pitch");
        var below = Aim3D.ComputeStep(camera, new Vector3(0, -2, 5), .01f, .01f, p);
        Expect(below.PitchErrorRadians < 0 && below.Pixels.Y < 0, "below target signed pitch");
        var signedSensitivity = Aim3D.ComputeStep(camera, new Vector3(2, 0, 5), -.01f, .01f, p);
        Expect(signedSensitivity.Pixels.X < 0, "negative yaw sensitivity preserves sign");
        Expect(!Aim3D.Project(camera, new Vector3(0, 0, -1)).IsVisible, "behind target is nonprojectable");
        var translated = new CameraFrame(new Vector3(10, 3, -4), Vector3.UnitZ, Vector3.UnitY, MathF.PI / 2, 1);
        var translatedProjection = Aim3D.Project(translated, new Vector3(11, 3, 1));
        Expect(MathF.Abs(translatedProjection.X - .2f) < 1e-4f, "translated camera parallax");
        ExpectThrows(() => Aim3D.ValidateCamera(new CameraFrame(Vector3.Zero, Vector3.Zero, Vector3.UnitY, 1, 1)), "zero forward");
        ExpectThrows(() => Aim3D.ValidateCamera(new CameraFrame(Vector3.Zero, new Vector3(float.MaxValue), Vector3.UnitY, 1, 1)), "overflowed forward");
        ExpectThrows(() => Aim3D.ValidateCamera(new CameraFrame(Vector3.Zero, Vector3.UnitZ, Vector3.UnitZ, 1, 1)), "degenerate basis");
        ExpectThrows(() => Aim3D.Project(camera, new Vector3(float.NaN, 0, 1)), "nonfinite target");
        ExpectThrows(() => Aim3D.ComputeStep(camera, new Vector3(0, 0, 1), 0, .01f, p), "zero sensitivity");
        var bounded = Aim3D.ComputeStep(camera, new Vector3(100, 100, 1), .01f, .01f, new AimParameters(2, 0, 1));
        Expect(float.IsFinite(bounded.Pixels.X) && float.IsFinite(bounded.Pixels.Y) && MathF.Abs(bounded.Pixels.X) <= 2 && MathF.Abs(bounded.Pixels.Y) <= 2, "finite pixel bound");
        // Simulate feedback by applying each returned pixel step to the optical basis while the target moves.
        var feedback = camera;
        var movingTarget = new Vector3(3, 1, 20);
        float initialError = float.PositiveInfinity, finalError = 0;
        for (int i = 0; i < 120; i++)
        {
            var step = Aim3D.ComputeStep(feedback, movingTarget, .01f, .01f, new AimParameters(8, .0005f, 0.8f));
            float error = MathF.Sqrt(step.YawErrorRadians * step.YawErrorRadians + step.PitchErrorRadians * step.PitchErrorRadians);
            if (i == 0) initialError = error;
            finalError = error;
            Vector3 rightAxis = Vector3.Normalize(Vector3.Cross(feedback.Up, feedback.Forward));
            Quaternion yawTurn = Quaternion.CreateFromAxisAngle(Vector3.Normalize(feedback.Up), step.Pixels.X * .01f);
            Quaternion pitchTurn = Quaternion.CreateFromAxisAngle(rightAxis, -step.Pixels.Y * .01f);
            Vector3 nextForward = Vector3.Normalize(Vector3.Transform(Vector3.Transform(feedback.Forward, yawTurn), pitchTurn));
            Vector3 nextUp = Vector3.Normalize(Vector3.Transform(Vector3.Transform(feedback.Up, yawTurn), pitchTurn));
            feedback = feedback with { Forward = nextForward, Up = nextUp };
            movingTarget.X += .002f; // a moving target, within the calibrated controller's response envelope
        }
        Expect(finalError < initialError && finalError < .02f, "bounded convergence with moving target");
    }

    private static void Expect(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("Aim3D check failed: " + name);
    }

    private static void ExpectThrows(Action action, string name)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Aim3D check failed: " + name);
    }
}
