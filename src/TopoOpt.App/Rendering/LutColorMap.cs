using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace TopoOpt.App.Rendering;

public enum LutKind
{
    Turbo,
    Viridis,
    CoolWarm
}

/// <summary>
/// High-performance 256-entry lookup table for perceptually-uniform colour mapping (Turbo, Viridis, CoolWarm).
/// Zero external dependency, instantaneous O(1) table lookup for large vertex sets.
/// </summary>
public static class LutColorMap
{
    private static readonly Vector3[] TurboLut = new Vector3[256];
    private static readonly Vector3[] ViridisLut = new Vector3[256];
    private static readonly Vector3[] CoolWarmLut = new Vector3[256];

    static LutColorMap()
    {
        for (int i = 0; i < 256; i++)
        {
            float t = i / 255.0f;
            TurboLut[i] = SampleTurboPolynomial(t);
            ViridisLut[i] = SampleViridis(t);
            CoolWarmLut[i] = SampleCoolWarm(t);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Evaluate(LutKind kind, float value, float min, float max)
    {
        float range = max - min;
        float t = range > 1e-12f ? Math.Clamp((value - min) / range, 0f, 1f) : 0.5f;
        int idx = (int)(t * 255.0f);

        return kind switch
        {
            LutKind.Turbo => TurboLut[idx],
            LutKind.Viridis => ViridisLut[idx],
            LutKind.CoolWarm => CoolWarmLut[idx],
            _ => TurboLut[idx]
        };
    }

    public static ReadOnlySpan<Vector3> GetLut(LutKind kind)
    {
        return kind switch
        {
            LutKind.Turbo => TurboLut,
            LutKind.Viridis => ViridisLut,
            LutKind.CoolWarm => CoolWarmLut,
            _ => TurboLut
        };
    }

    // Google Turbo colormap 7th-order polynomial approximation (Mikhailov 2019)
    private static Vector3 SampleTurboPolynomial(float t)
    {
        float r = 0.13572138f + t * (4.61539260f + t * (-42.66032258f + t * (132.13108234f + t * (-152.94239396f + t * 59.28637943f))));
        float g = 0.09140261f + t * (2.19418839f + t * (4.84296658f + t * (-14.18503327f + t * (4.27729857f + t * 2.82956604f))));
        float b = 0.10667330f + t * (12.64194608f + t * (-60.58204836f + t * (110.36276771f + t * (-89.90310912f + t * 27.34824973f))));

        return new Vector3(Math.Clamp(r, 0f, 1f), Math.Clamp(g, 0f, 1f), Math.Clamp(b, 0f, 1f));
    }

    private static Vector3 SampleViridis(float t)
    {
        Vector3 c0 = new(0.267f, 0.004f, 0.329f);
        Vector3 c1 = new(0.229f, 0.322f, 0.545f);
        Vector3 c2 = new(0.127f, 0.566f, 0.550f);
        Vector3 c3 = new(0.369f, 0.788f, 0.382f);
        Vector3 c4 = new(0.993f, 0.906f, 0.144f);

        if (t <= 0.25f) return Vector3.Lerp(c0, c1, t * 4f);
        if (t <= 0.50f) return Vector3.Lerp(c1, c2, (t - 0.25f) * 4f);
        if (t <= 0.75f) return Vector3.Lerp(c2, c3, (t - 0.50f) * 4f);
        return Vector3.Lerp(c3, c4, (t - 0.75f) * 4f);
    }

    private static Vector3 SampleCoolWarm(float t)
    {
        Vector3 c0 = new(0.230f, 0.299f, 0.754f);
        Vector3 c1 = new(0.865f, 0.865f, 0.865f);
        Vector3 c2 = new(0.706f, 0.016f, 0.150f);

        return t < 0.5f
            ? Vector3.Lerp(c0, c1, t * 2f)
            : Vector3.Lerp(c1, c2, (t - 0.5f) * 2f);
    }
}

