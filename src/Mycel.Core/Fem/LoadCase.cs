using System;
using System.Numerics;
using Mycel.Core.Voxelization;

namespace Mycel.Core.Fem;

/// <summary>
/// A load case defining a total force distributed across a boundary surface patch.
/// </summary>
public sealed class LoadCase
{
    public SurfacePatch Patch { get; }
    public Vector3 TotalForce { get; }

    public LoadCase(SurfacePatch patch, Vector3 totalForce)
    {
        Patch = patch ?? throw new ArgumentNullException(nameof(patch));
        TotalForce = totalForce;
    }
}
