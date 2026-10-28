using System;

namespace Mycel.Core.Optimization;

public readonly record struct IterationTimings(
    double SolveMs,
    double SensitivityMs,
    double FilterMs,
    double UpdateMs,
    double TotalMs);

public sealed class IterationSnapshot
{
    public int Iteration { get; init; }
    public double Compliance { get; init; }
    public double VolumeFraction { get; init; }
    public double MaxChange { get; init; }
    public double GreyLevel { get; init; }
    public int SolverIterations { get; init; }
    public double SolverResidual { get; init; }
    public IterationTimings Timings { get; init; }
    public float[] PhysicalDensity { get; init; } = Array.Empty<float>();
    public float[] DesignDensity { get; init; } = Array.Empty<float>();
    public double MaxDisplacement { get; init; }

    public double SolveMs => Timings.SolveMs;
    public double SensitivityMs => Timings.SensitivityMs;
    public double FilterMs => Timings.FilterMs;
    public double UpdateMs => Timings.UpdateMs;
    public double TotalMs => Timings.TotalMs;
}
