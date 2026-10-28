using System;
using System.Numerics;

namespace Mycel.Core.Geometry;

/// <summary>
/// Diagnostic summary produced when importing and welding a triangle mesh.
/// </summary>
public readonly record struct MeshReport(
    int Triangles,
    int Vertices,
    int DegenerateSkipped,
    int VerticesMerged,
    int OpenEdges,
    int NonManifoldEdges,
    bool IsWatertight,
    double Volume,
    (Vector3 Min, Vector3 Max) BBox,
    LengthUnit SuspectedUnits,
    TimeSpan LoadDuration);

