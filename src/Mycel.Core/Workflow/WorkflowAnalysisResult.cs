using System;

namespace Mycel.Core.Workflow;

public sealed class WorkflowAnalysisResult
{
    public double Compliance { get; init; }
    public double MaxDisplacement { get; init; }
    public double MaxVonMises { get; init; }
    public double P99VonMises { get; init; }
    public double SafetyFactor { get; init; }
    public double SafetyFactorP99 { get; init; }

    public float[] VertexVonMises { get; init; } = Array.Empty<float>();
    public float[] VertexDisplacements { get; init; } = Array.Empty<float>();

    public double MinVonMises { get; init; }
    public double MinDisplacement { get; init; }

    public int SolverIterations { get; init; }
    public double SolverResidual { get; init; }
    public double ElapsedMilliseconds { get; init; }

    public bool IsOutdated { get; set; }
}

