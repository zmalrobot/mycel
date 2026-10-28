using System;
using System.Numerics;
using Mycel.App.Diagnostics;

namespace Mycel.App.Rendering;

/// <summary>
/// Right-handed Z-up turntable perspective camera supporting orbit, pan, zoom-to-cursor,
/// fit-to-view, and screen-to-world ray unprojection.
/// </summary>
public sealed class OrbitCamera
{
    public Vector3 Target { get; set; } = Vector3.Zero;
    public float Distance { get; set; } = 100.0f;
    public float AzimuthRadians { get; set; } = -MathF.PI * 0.35f;
    public float ElevationRadians { get; set; } = MathF.PI * 0.22f;
    public float FovYRadians { get; set; } = 45.0f * (MathF.PI / 180.0f);
    public float BoundingRadius { get; private set; } = 50.0f;

    public float NearPlane => MathF.Max(0.01f, MathF.Min(Distance * 0.02f, BoundingRadius * 0.01f));
    public float FarPlane => MathF.Max(100.0f, Distance + BoundingRadius * 8.0f);

    public Vector3 GetEyePosition()
    {
        float cosElev = MathF.Cos(ElevationRadians);
        float sinElev = MathF.Sin(ElevationRadians);
        float cosAzim = MathF.Cos(AzimuthRadians);
        float sinAzim = MathF.Sin(AzimuthRadians);

        Vector3 offset = new(
            Distance * cosElev * cosAzim,
            Distance * cosElev * sinAzim,
            Distance * sinElev);

        return Target + offset;
    }

    public void GetCameraBasis(out Vector3 forward, out Vector3 right, out Vector3 up)
    {
        float cosElev = MathF.Cos(ElevationRadians);
        float sinElev = MathF.Sin(ElevationRadians);
        float cosAzim = MathF.Cos(AzimuthRadians);
        float sinAzim = MathF.Sin(AzimuthRadians);

        // Eye -> Target direction
        forward = Vector3.Normalize(new Vector3(
            -cosElev * cosAzim,
            -cosElev * sinAzim,
            -sinElev));

        // Right vector in XY plane perpendicular to azimuth
        right = Vector3.Normalize(new Vector3(-sinAzim, cosAzim, 0f));
        up = Vector3.Normalize(Vector3.Cross(right, forward));
    }

    public Matrix4x4 GetViewMatrix()
    {
        Vector3 eye = GetEyePosition();
        GetCameraBasis(out _, out _, out Vector3 up);
        return Matrix4x4.CreateLookAt(eye, Target, up);
    }

    public Matrix4x4 GetProjectionMatrix(float aspectRatio)
    {
        float safeAspect = MathF.Max(0.05f, aspectRatio);
        return Matrix4x4.CreatePerspectiveFieldOfView(FovYRadians, safeAspect, NearPlane, FarPlane);
    }

    public void FitToBounds(Vector3 min, Vector3 max)
    {
        Target = (min + max) * 0.5f;
        Vector3 extents = max - min;
        float radius = extents.Length() * 0.5f;
        if (radius < 1e-4f || float.IsNaN(radius) || float.IsInfinity(radius))
        {
            radius = 25.0f;
        }

        BoundingRadius = radius;
        float halfFov = FovYRadians * 0.5f;
        Distance = (radius / MathF.Sin(halfFov)) * 1.15f;
        AzimuthRadians = -MathF.PI * 0.35f;
        ElevationRadians = MathF.PI * 0.22f;
    }

    public void Orbit(float deltaPixelX, float deltaPixelY)
    {
        const float Sensitivity = 0.0065f;
        AzimuthRadians -= deltaPixelX * Sensitivity;
        ElevationRadians = Math.Clamp(
            ElevationRadians + deltaPixelY * Sensitivity,
            -MathF.PI * 0.495f,
            +MathF.PI * 0.495f);
        OrbitDiagnostics.RecordCameraOrbit(deltaPixelX, deltaPixelY, AzimuthRadians, ElevationRadians);
    }

    public void Pan(float deltaPixelX, float deltaPixelY, float viewportHeight)
    {
        if (viewportHeight <= 1f) return;

        GetCameraBasis(out _, out Vector3 right, out Vector3 up);
        float worldUnitsPerPixel = (2.0f * Distance * MathF.Tan(FovYRadians * 0.5f)) / viewportHeight;
        Target -= (right * deltaPixelX - up * deltaPixelY) * worldUnitsPerPixel;
        OrbitDiagnostics.RecordCameraPan(deltaPixelX, deltaPixelY, Target.X, Target.Y, Target.Z);
    }

    public void ZoomToCursor(float wheelSteps, Vector2 pixelPos, float viewportWidth, float viewportHeight)
    {
        if (viewportWidth <= 1f || viewportHeight <= 1f) return;

        var (rayOrigin, rayDir) = UnprojectRay(pixelPos.X, pixelPos.Y, viewportWidth, viewportHeight);
        GetCameraBasis(out Vector3 forward, out _, out _);

        // Intersect cursor ray with plane passing through Target perpendicular to view forward
        float denom = Vector3.Dot(rayDir, forward);
        Vector3 focusPoint = MathF.Abs(denom) > 1e-5f
            ? rayOrigin + rayDir * (Vector3.Dot(Target - rayOrigin, forward) / denom)
            : Target;

        float zoomFactor = MathF.Pow(0.86f, wheelSteps);
        float minDistance = MathF.Max(0.05f, BoundingRadius * 0.02f);
        float maxDistance = BoundingRadius * 50.0f;
        float newDistance = Math.Clamp(Distance * zoomFactor, minDistance, maxDistance);
        float actualRatio = newDistance / Distance;

        Target = focusPoint + (Target - focusPoint) * actualRatio;
        Distance = newDistance;
    }

    public (Vector3 Origin, Vector3 Direction) UnprojectRay(float pixelX, float pixelY, float viewportWidth, float viewportHeight)
    {
        float aspect = MathF.Max(0.05f, viewportWidth / MathF.Max(1f, viewportHeight));
        float ndcX = (2.0f * pixelX / MathF.Max(1f, viewportWidth)) - 1.0f;
        float ndcY = 1.0f - (2.0f * pixelY / MathF.Max(1f, viewportHeight));

        float tanHalfFov = MathF.Tan(FovYRadians * 0.5f);
        float viewX = ndcX * aspect * tanHalfFov;
        float viewY = ndcY * tanHalfFov;

        Vector3 eye = GetEyePosition();
        GetCameraBasis(out Vector3 forward, out Vector3 right, out Vector3 up);

        Vector3 worldDir = Vector3.Normalize(forward + right * viewX + up * viewY);
        return (eye, worldDir);
    }
}

