using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace TopoOpt.App.Rendering;

public enum ColorMapKind
{
    Viridis,
    Turbo,
    CoolWarm,
    Jet,
    DensityGrey
}

public static class ColorMap
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Evaluate(ColorMapKind kind, float value, float min, float max)
    {
        float range = max - min;
        float t = range > 1e-12f ? Math.Clamp((value - min) / range, 0f, 1f) : 0.5f;
        return kind switch
        {
            ColorMapKind.Viridis => Viridis(t),
            ColorMapKind.Turbo => Turbo(t),
            ColorMapKind.CoolWarm => CoolWarm(t),
            ColorMapKind.Jet => Jet(t),
            ColorMapKind.DensityGrey => new Vector3(t, t, t),
            _ => Viridis(t)
        };
    }

    private static Vector3 Viridis(float t)
    {
        Vector3 c0 = new(0.267f, 0.004f, 0.329f);
        Vector3 c1 = new(0.229f, 0.322f, 0.545f);
        Vector3 c2 = new(0.127f, 0.566f, 0.550f);
        Vector3 c3 = new(0.369f, 0.788f, 0.382f);
        Vector3 c4 = new(0.993f, 0.906f, 0.144f);
        return Sample5(t, c0, c1, c2, c3, c4);
    }

    private static Vector3 Turbo(float t)
    {
        Vector3 c0 = new(0.18995f, 0.07176f, 0.23217f);
        Vector3 c1 = new(0.19483f, 0.53339f, 0.98883f);
        Vector3 c2 = new(0.29804f, 0.94902f, 0.59608f);
        Vector3 c3 = new(0.96863f, 0.67451f, 0.14510f);
        Vector3 c4 = new(0.47960f, 0.01583f, 0.01055f);
        return Sample5(t, c0, c1, c2, c3, c4);
    }

    private static Vector3 CoolWarm(float t)
    {
        Vector3 c0 = new(0.230f, 0.299f, 0.754f);
        Vector3 c2 = new(0.865f, 0.865f, 0.865f);
        Vector3 c4 = new(0.706f, 0.016f, 0.150f);
        return t < 0.5f
            ? Vector3.Lerp(c0, c2, t * 2f)
            : Vector3.Lerp(c2, c4, (t - 0.5f) * 2f);
    }

    private static Vector3 Jet(float t)
    {
        float r = Math.Clamp(1.5f - MathF.Abs(4f * t - 3f), 0f, 1f);
        float g = Math.Clamp(1.5f - MathF.Abs(4f * t - 2f), 0f, 1f);
        float b = Math.Clamp(1.5f - MathF.Abs(4f * t - 1f), 0f, 1f);
        return new Vector3(r, g, b);
    }

    private static Vector3 Sample5(float t, Vector3 c0, Vector3 c1, Vector3 c2, Vector3 c3, Vector3 c4)
    {
        float s = t * 4f;
        int idx = Math.Clamp((int)s, 0, 3);
        float frac = s - idx;
        return idx switch
        {
            0 => Vector3.Lerp(c0, c1, frac),
            1 => Vector3.Lerp(c1, c2, frac),
            2 => Vector3.Lerp(c2, c3, frac),
            _ => Vector3.Lerp(c3, c4, frac)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector4 UnpackRgba(uint rgba)
    {
        float r = ((rgba >> 24) & 0xFFu) / 255.0f;
        float g = ((rgba >> 16) & 0xFFu) / 255.0f;
        float b = ((rgba >> 8) & 0xFFu) / 255.0f;
        float a = (rgba & 0xFFu) / 255.0f;
        return new Vector4(r, g, b, a);
    }
}

