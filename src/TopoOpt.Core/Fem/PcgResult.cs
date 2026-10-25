using System;

namespace TopoOpt.Core.Fem;

public sealed record PcgProgress(int Iteration, double RelativeResidual);

public sealed record PcgResult
{
    public int Iterations { get; init; }
    public double FinalResidual { get; init; }
    public bool Converged { get; init; }
    public TimeSpan Elapsed { get; init; }
    public string? Message { get; init; }
}
