using System;
using System.Collections.Generic;
using System.Numerics;

namespace Mycel.Core.Voxelization;

public enum FaceDirection
{
    NegX = 0,
    PosX = 1,
    NegY = 2,
    PosY = 3,
    NegZ = 4,
    PosZ = 5
}

/// <summary>
/// Identifies an exposed face of a Hex8 element.
/// </summary>
public readonly record struct VoxelFace(int ElementIndex, FaceDirection Direction, Vector3 Center, Vector3 Normal);

/// <summary>
/// A surface patch mapped from selected surface triangles to exposed voxel faces and boundary nodes.
/// </summary>
public sealed class SurfacePatch
{
    public IReadOnlyList<VoxelFace> Faces { get; }
    public int[] Nodes { get; }
    public double[] NodeArea { get; }
    public (Vector3 Min, Vector3 Max) BBox { get; }
    public bool TouchesLargestComponent { get; }

    public int FaceCount => Faces.Count;
    public int NodeCount => Nodes.Length;

    public SurfacePatch(
        IReadOnlyList<VoxelFace> Faces,
        int[] Nodes,
        double[] NodeArea,
        (Vector3 Min, Vector3 Max) BBox,
        bool TouchesLargestComponent)
    {
        this.Faces = Faces ?? Array.Empty<VoxelFace>();
        this.Nodes = Nodes ?? Array.Empty<int>();
        this.NodeArea = NodeArea ?? Array.Empty<double>();
        this.BBox = BBox;
        this.TouchesLargestComponent = TouchesLargestComponent;
    }
}

