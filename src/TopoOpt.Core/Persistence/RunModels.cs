using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Serialization;
using TopoOpt.Core.Geometry;
using TopoOpt.Core.Voxelization;
using TopoOpt.Core.Workflow;

namespace TopoOpt.Core.Persistence;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RunStatus
{
    Running,
    Completed,
    Stopped,
    Failed,
    Interrupted
}

public sealed record GridSpecDto(
    float OriginX,
    float OriginY,
    float OriginZ,
    double H,
    int Nx,
    int Ny,
    int Nz)
{
    public static GridSpecDto FromGrid(GridSpec g) =>
        new(g.Origin.X, g.Origin.Y, g.Origin.Z, g.H, g.Nx, g.Ny, g.Nz);

    public GridSpec ToGrid() =>
        new(new Vector3(OriginX, OriginY, OriginZ), (float)H, Nx, Ny, Nz);
}

public sealed record OptimizerOptionsDto(
    double VolumeFraction,
    double FilterRadiusMm,
    double Penal,
    double MoveLimit,
    int MaxIterations,
    double EminRatio,
    bool Continuation,
    bool EnableHeaviside,
    bool ProtectBoundaryHinges,
    int KeepEveryN = 1);

public sealed record CompactLoadNodeSetDto(
    string Name,
    int[] Nodes,
    double[] NodeAreas,
    float ForceX,
    float ForceY,
    float ForceZ);

public sealed record CompactRunNodeSetsDto(
    int[] FixedDofs,
    List<CompactLoadNodeSetDto> Loads);

public sealed class RunMetadata
{
    public int FormatVersion { get; set; } = 1;
    public string AppVersion { get; set; } = "1.0.0";
    public string RunName { get; set; } = "run";
    public string InputStlSha256 { get; set; } = string.Empty;
    public GridSpecDto Grid { get; set; } = new(0, 0, 0, 1, 1, 1, 1);
    public MaterialDefinition Material { get; set; } = MaterialDefinition.CreatePreset(MaterialPresetKind.Pla);
    public OptimizerOptionsDto Options { get; set; } = new(0.3, 1.2, 3.0, 0.2, 100, 1e-6, false, false, true, 1);
    public CompactRunNodeSetsDto NodeSets { get; set; } = new([], []);
    public RunStatus Status { get; set; } = RunStatus.Running;
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public int CompletedIterations { get; set; }
    public long TotalDiskBytes { get; set; }
    public double OriginalSolidCompliance { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed record IterationHeader(
    int Version,
    int Nx,
    int Ny,
    int Nz,
    int Iteration,
    double Compliance,
    double VolumeFraction,
    double MaxChange,
    double GreyLevel,
    int SolverIterations,
    double IterationMs,
    uint Checksum);

public sealed record IterationMetricRow(
    int Iteration,
    double Compliance,
    double VolumeFraction,
    double MaxChange,
    double GreyLevel,
    int SolverIterations,
    double SolverResidual,
    double TotalMs,
    long DiskBytes);

public sealed class VerificationResult
{
    public required int Iteration { get; init; }
    public required float Threshold { get; init; }
    public required IndexedMesh IsoMesh { get; init; }
    public required float[] VertexVonMises { get; init; }
    public required float[] VertexDisplacements { get; init; }
    public required double MaxVonMises { get; init; }
    public required double MinVonMises { get; init; }
    public required double P99VonMises { get; init; }
    public required double MaxDisplacement { get; init; }
    public required double MinDisplacement { get; init; }
    public required double Compliance { get; init; }
    public required double OriginalSolidCompliance { get; init; }
    public required double ComplianceRatio { get; init; }
    public required double MaterialSavedPercent { get; init; }
    public required double SafetyFactorMax { get; init; }
    public required double SafetyFactorP99 { get; init; }
    public required int SolidElementCount { get; init; }
    public required int DomainElementCount { get; init; }
    public string Disclaimer { get; init; } =
        "Indicative results. FDM parts are anisotropic and material data vary: verify load-bearing parts with physical tests.";
}
