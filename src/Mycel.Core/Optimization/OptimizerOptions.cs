using System;

namespace Mycel.Core.Optimization;

public sealed record OptimizerOptions(
    double VolumeFraction = 0.4,
    double FilterRadiusMm = 2.0,
    double Penal = 3.0,
    double MoveLimit = 0.2,
    int MaxIterations = 200,
    double SolverToleranceInitial = 1e-4,
    double SolverToleranceFinal = 1e-6,
    int SolverMaxIterations = 5000,
    double EminRatio = 1e-6,
    bool Continuation = false,
    bool EnableHeaviside = false,
    int RngSeed = 42,
    double RhoMin = 1e-3,
    byte[]? PassiveMask = null,
    bool ProtectBoundaryHinges = true,
    float[]? InitialDensity = null,
    int StartIteration = 1,
    double[]? WarmStartDisplacements = null,
    bool EnforceMinimumFilterRadius = true,
    IterationController? Controller = null
);
