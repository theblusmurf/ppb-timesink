using System.Numerics;

namespace PoteHunter;

/// <summary>Verified optical camera state. Forward is the viewing direction; Up is world-up on screen.</summary>
/// <remarks>
/// Coordinates are engine coordinates (X, height, Z): an entity mapped from xbot, heightbot, ybot is
/// <c>new Vector3(xbot, heightbot, ybot)</c>. Screen X is right-positive and screen Y is up-positive.
/// This frame describes the camera only; it has no relationship to PlayerHeading.
/// </remarks>
public readonly record struct CameraFrame(
    Vector3 Position,
    Vector3 Forward,
    Vector3 Up,
    float VerticalFovRadians,
    float AspectRatio);

/// <summary>Explicit control inputs for a bounded relative mouse correction.</summary>
public readonly record struct AimParameters(
    float PixelLimit,
    float AngularDeadzoneRadians,
    float Gain)
{
    public void Validate()
    {
        if (!float.IsFinite(PixelLimit) || PixelLimit <= 0 || PixelLimit > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(PixelLimit));
        if (!float.IsFinite(AngularDeadzoneRadians) || AngularDeadzoneRadians < 0)
            throw new ArgumentOutOfRangeException(nameof(AngularDeadzoneRadians));
        if (!float.IsFinite(Gain) || Gain <= 0)
            throw new ArgumentOutOfRangeException(nameof(Gain));
    }
}

/// <summary>Normalized projection, with both coordinates in the camera's right/up convention.</summary>
public readonly record struct ScreenProjection(float X, float Y, float Depth, bool IsVisible)
{
    public bool Finite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Depth);
}

/// <summary>Angular error and the bounded relative mouse step derived from it.</summary>
public readonly record struct AimStep(
    Vector2 Pixels,
    float YawErrorRadians,
    float PitchErrorRadians,
    ScreenProjection Projection,
    bool WithinDeadzone)
{
    public bool Finite => float.IsFinite(Pixels.X) && float.IsFinite(Pixels.Y) &&
        float.IsFinite(YawErrorRadians) && float.IsFinite(PitchErrorRadians);
}

public static class Aim3D
{
    private const float BasisTolerance = 0.002f;
    private const float MinimumDepth = 1e-5f;

    public static void ValidateCamera(in CameraFrame camera)
    {
        RequireFinite(camera.Position, nameof(camera.Position));
        RequireFinite(camera.Forward, nameof(camera.Forward));
        RequireFinite(camera.Up, nameof(camera.Up));
        if (!float.IsFinite(camera.VerticalFovRadians) || camera.VerticalFovRadians <= 0 || camera.VerticalFovRadians >= MathF.PI)
            throw new ArgumentOutOfRangeException(nameof(camera.VerticalFovRadians));
        if (!float.IsFinite(camera.AspectRatio) || camera.AspectRatio <= 0)
            throw new ArgumentOutOfRangeException(nameof(camera.AspectRatio));
        if (!float.IsFinite(camera.Forward.LengthSquared()) || !float.IsFinite(camera.Up.LengthSquared()) || camera.Forward.LengthSquared() < BasisTolerance * BasisTolerance || camera.Up.LengthSquared() < BasisTolerance * BasisTolerance)
            throw new ArgumentException("Camera basis vectors must be nonzero.", nameof(camera));
        Vector3 forward = NormalizeBasis(camera.Forward, nameof(camera.Forward));
        Vector3 up = NormalizeBasis(camera.Up, nameof(camera.Up));
        if (MathF.Abs(Vector3.Dot(forward, up)) > BasisTolerance)
            throw new ArgumentException("Camera Forward and Up must be orthogonal.", nameof(camera));
        if (Vector3.Cross(up, forward).LengthSquared() < BasisTolerance * BasisTolerance)
            throw new ArgumentException("Camera basis must define a right axis.", nameof(camera));
    }

    /// <summary>Projects a target to normalized camera coordinates; X right and Y up, each centered at zero.</summary>
    public static ScreenProjection Project(in CameraFrame camera, Vector3 target)
    {
        ValidateCamera(camera);
        RequireFinite(target, nameof(target));
        Vector3 forward = NormalizeBasis(camera.Forward, nameof(camera.Forward));
        Vector3 up = NormalizeBasis(camera.Up, nameof(camera.Up));
        Vector3 right = Vector3.Normalize(Vector3.Cross(up, forward));
        Vector3 relative = target - camera.Position;
        if (!float.IsFinite(relative.X) || !float.IsFinite(relative.Y) || !float.IsFinite(relative.Z))
            throw new ArgumentException("Target-camera displacement overflowed.", nameof(target));
        float depth = Vector3.Dot(relative, forward);
        if (!float.IsFinite(depth) || depth <= MinimumDepth)
            return new ScreenProjection(float.NaN, float.NaN, depth, false);
        float x = Vector3.Dot(relative, right);
        float y = Vector3.Dot(relative, up);
        float tanHalf = MathF.Tan(camera.VerticalFovRadians * 0.5f);
        float normalizedX = x / (depth * tanHalf * camera.AspectRatio);
        float normalizedY = y / (depth * tanHalf);
        bool insideFrustum = float.IsFinite(normalizedX) && float.IsFinite(normalizedY) && MathF.Abs(normalizedX) <= 1 && MathF.Abs(normalizedY) <= 1;
        return new ScreenProjection(normalizedX, normalizedY, depth, insideFrustum);
    }

    /// <summary>
    /// Computes positive yaw for a target to the camera's right and positive pitch for a target above it.
    /// sensitivities are calibrated radians per mouse pixel and may be signed, preserving their sign.
    /// </summary>
    public static AimStep ComputeStep(in CameraFrame camera, Vector3 target, float yawRadiansPerPixel,
        float pitchRadiansPerPixel, in AimParameters parameters)
    {
        ValidateCamera(camera);
        RequireFinite(target, nameof(target));
        parameters.Validate();
        ValidateSensitivity(yawRadiansPerPixel, nameof(yawRadiansPerPixel));
        ValidateSensitivity(pitchRadiansPerPixel, nameof(pitchRadiansPerPixel));
        Vector3 forward = NormalizeBasis(camera.Forward, nameof(camera.Forward));
        Vector3 up = NormalizeBasis(camera.Up, nameof(camera.Up));
        Vector3 right = Vector3.Normalize(Vector3.Cross(up, forward));
        Vector3 delta = target - camera.Position;
        if (!float.IsFinite(delta.X) || !float.IsFinite(delta.Y) || !float.IsFinite(delta.Z))
            throw new ArgumentException("Target-camera displacement overflowed.", nameof(target));
        float z = Vector3.Dot(delta, forward);
        if (!float.IsFinite(z) || z <= MinimumDepth)
            throw new ArgumentException("Target is behind or on the camera plane.", nameof(target));
        float x = Vector3.Dot(delta, right), y = Vector3.Dot(delta, up);
        float yaw = MathF.Atan2(x, z);
        float pitch = MathF.Atan2(y, MathF.Sqrt(x * x + z * z));
        bool dead = MathF.Abs(yaw) <= parameters.AngularDeadzoneRadians && MathF.Abs(pitch) <= parameters.AngularDeadzoneRadians;
        float px = dead || MathF.Abs(yaw) <= parameters.AngularDeadzoneRadians ? 0 : yaw / yawRadiansPerPixel * parameters.Gain;
        float py = dead || MathF.Abs(pitch) <= parameters.AngularDeadzoneRadians ? 0 : pitch / pitchRadiansPerPixel * parameters.Gain;
        px = Math.Clamp(px, -parameters.PixelLimit, parameters.PixelLimit);
        py = Math.Clamp(py, -parameters.PixelLimit, parameters.PixelLimit);
        return new AimStep(new Vector2(px, py), yaw, pitch, Project(camera, target), dead);
    }

    private static void ValidateSensitivity(float value, string name)
    {
        if (!float.IsFinite(value) || MathF.Abs(value) < 1e-7f)
            throw new ArgumentOutOfRangeException(name, "Sensitivity must be finite and nonzero.");
    }

    private static void RequireFinite(Vector3 value, string name)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentException("Vector must contain finite components.", name);
    }

    private static Vector3 NormalizeBasis(Vector3 value, string name)
    {
        float lengthSquared = value.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared < BasisTolerance * BasisTolerance)
            throw new ArgumentException("Basis vector cannot be normalized safely.", name);
        Vector3 result = Vector3.Normalize(value);
        if (!float.IsFinite(result.X) || !float.IsFinite(result.Y) || !float.IsFinite(result.Z) || result.LengthSquared() < .5f)
            throw new ArgumentException("Basis vector normalization overflowed.", name);
        return result;
    }
}
