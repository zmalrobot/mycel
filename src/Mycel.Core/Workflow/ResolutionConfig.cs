using System;
using System.Numerics;
using Mycel.Core.Geometry;

namespace Mycel.Core.Workflow;

public enum ResolutionPreset
{
    Draft,
    Standard,
    Fine,
    Custom
}

public sealed class ResolutionConfig
{
    public ResolutionPreset Preset { get; set; } = ResolutionPreset.Standard;
    public int TargetSolidElements { get; set; } = 100_000;
    public float ExplicitH { get; set; } = 0.0f; // If > 0, manual h override

    public static int GetPresetTargetElements(ResolutionPreset preset)
    {
        return preset switch
        {
            ResolutionPreset.Draft => 50_000,
            ResolutionPreset.Standard => 100_000,
            ResolutionPreset.Fine => 250_000,
            _ => 100_000
        };
    }

    public static double EstimateSolveTimeSeconds(int targetElements)
    {
        // Grounded in docs/STEP3_BENCH.md:
        // ~50k -> 2.2s, ~100k -> 4.5s, ~200k -> 12s, ~500k -> 35s
        if (targetElements <= 50_000) return 2.2;
        if (targetElements <= 100_000) return 4.5;
        if (targetElements <= 250_000) return 12.0;
        return 35.0 * (targetElements / 500_000.0);
    }

    public static double EstimateMemoryMb(int targetElements)
    {
        // Grounded in docs/STEP3_BENCH.md:
        // ~50k -> 47 MB, ~100k -> 95 MB, ~200k -> 395 MB, ~500k -> 415 MB
        if (targetElements <= 50_000) return 50.0;
        if (targetElements <= 100_000) return 100.0;
        if (targetElements <= 250_000) return 400.0;
        return 500.0 * (targetElements / 500_000.0);
    }

    public static float ComputeVoxelSize(IndexedMesh mesh, int targetElements)
    {
        if (mesh == null || targetElements <= 0)
            return 1.0f;

        double volume = Math.Abs(mesh.SignedVolume);
        if (volume <= 1e-6)
        {
            // Fallback to bounding box volume if mesh is non-watertight
            var (min, max) = mesh.BBox;
            Vector3 size = max - min;
            volume = Math.Max(1e-3, (double)size.X * size.Y * size.Z * 0.5);
        }

        double h = Math.Cbrt(volume / targetElements);
        return (float)Math.Clamp(h, 0.05, 100.0);
    }
}
