using System;
using System.Collections.Generic;

namespace TopoOpt.Core.Optimization;

public sealed class TopologyOptimizationResult
{
    public bool Converged { get; init; }
    public int TotalIterations { get; init; }
    public IterationSnapshot FinalSnapshot { get; init; } = new();
    public IReadOnlyList<IterationSnapshot> Snapshots { get; init; } = Array.Empty<IterationSnapshot>();
    public double FinalCompliance { get; init; }
    public double FinalVolumeFraction { get; init; }
    public float[] FinalPhysicalDensity { get; init; } = Array.Empty<float>();
    public double[] FinalDisplacements { get; init; } = Array.Empty<double>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
