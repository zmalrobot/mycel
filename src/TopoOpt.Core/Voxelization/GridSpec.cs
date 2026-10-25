using System;
using System.Numerics;
using TopoOpt.Core.Geometry;

namespace TopoOpt.Core.Voxelization;

/// <summary>
/// Defines a regular 3D structured grid of cubic hexahedral elements (Hex8).
/// Coordinates are in millimetres (mm) with Z pointing upward.
/// </summary>
public sealed class GridSpec
{
    public const long DefaultMaxElements = 8_000_000;

    public Vector3 Origin { get; }
    public float H { get; }
    public int Nx { get; }
    public int Ny { get; }
    public int Nz { get; }

    public long TotalElements => (long)Nx * Ny * Nz;
    public long TotalNodes => (long)(Nx + 1) * (Ny + 1) * (Nz + 1);

    public GridSpec(Vector3 origin, float h, int nx, int ny, int nz, long maxElements = long.MaxValue)
    {
        if (h <= 0f)
            throw new ArgumentOutOfRangeException(nameof(h), "Voxel pitch must be positive.");
        if (nx <= 0 || ny <= 0 || nz <= 0)
            throw new ArgumentOutOfRangeException("Grid dimensions must be strictly positive.");

        long total = (long)nx * ny * nz;
        if (total > maxElements)
        {
            throw new InvalidOperationException(
                $"Grid elements count ({total:N0}) exceeds configurable cap of {maxElements:N0} elements. " +
                "Please increase the voxel size h or adjust the element limit.");
        }

        Origin = origin;
        H = h;
        Nx = nx;
        Ny = ny;
        Nz = nz;
    }

    /// <summary>
    /// Constructs a grid covering the mesh bounding box with a specific voxel size h and optional voxel padding.
    /// </summary>
    public static GridSpec FromVoxelSize(IndexedMesh mesh, float h, int padding = 0, long maxElements = DefaultMaxElements)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (h <= 0f)
            throw new ArgumentOutOfRangeException(nameof(h), "Voxel pitch must be positive.");
        if (padding < 0)
            throw new ArgumentOutOfRangeException(nameof(padding), "Padding must be non-negative.");

        var bbox = mesh.BBox;
        Vector3 span = bbox.Max - bbox.Min;

        int nx = (int)Math.Ceiling(span.X / h) + 2 * padding;
        int ny = (int)Math.Ceiling(span.Y / h) + 2 * padding;
        int nz = (int)Math.Ceiling(span.Z / h) + 2 * padding;

        nx = Math.Max(1, nx);
        ny = Math.Max(1, ny);
        nz = Math.Max(1, nz);

        long total = (long)nx * ny * nz;
        if (total > maxElements)
        {
            throw new InvalidOperationException(
                $"Grid elements count ({total:N0}) exceeds configurable cap of {maxElements:N0} elements. " +
                "Please increase the voxel size h or adjust the element limit.");
        }

        // Center the grid around the mesh bounding box
        Vector3 gridSpan = new(nx * h, ny * h, nz * h);
        Vector3 origin = bbox.Min - new Vector3(padding * h) - (gridSpan - span - new Vector3(2 * padding * h)) * 0.5f;

        return new GridSpec(origin, h, nx, ny, nz, maxElements);
    }

    /// <summary>
    /// Constructs a grid sizing voxel pitch h so that targetSolidElements approximate the mesh solid volume.
    /// Formula: h = (meshVolume / targetSolidElements)^(1/3).
    /// </summary>
    public static GridSpec FromTargetElementCount(IndexedMesh mesh, int targetSolidElements, int padding = 0, long maxElements = DefaultMaxElements)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (targetSolidElements <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetSolidElements), "Target element count must be positive.");

        double bboxVol = (mesh.BBox.Max.X - mesh.BBox.Min.X) * (mesh.BBox.Max.Y - mesh.BBox.Min.Y) * (mesh.BBox.Max.Z - mesh.BBox.Min.Z);
        double volume = mesh.SignedVolume > 0 ? mesh.SignedVolume : bboxVol;
        if (volume <= 0)
        {
            volume = 1.0;
        }

        double hEst = Math.Cbrt(volume / targetSolidElements);
        float h = (float)Math.Max(1e-4, hEst);

        return FromVoxelSize(mesh, h, padding, maxElements);
    }

    /// <summary>
    /// Computes the flat element index e = ix + Nx * (iy + Ny * iz).
    /// </summary>
    public int ElementIndex(int ix, int iy, int iz)
    {
        if ((uint)ix >= (uint)Nx || (uint)iy >= (uint)Ny || (uint)iz >= (uint)Nz)
            throw new ArgumentOutOfRangeException($"Element coordinates ({ix}, {iy}, {iz}) out of bounds ({Nx}, {Ny}, {Nz}).");

        return ix + Nx * (iy + Ny * iz);
    }

    /// <summary>
    /// Computes the flat node index n = i + (Nx + 1) * (j + (Ny + 1) * k).
    /// </summary>
    public long NodeIndex(int i, int j, int k)
    {
        long nx1 = Nx + 1;
        long ny1 = Ny + 1;
        long nz1 = Nz + 1;

        if ((uint)i >= (uint)nx1 || (uint)j >= (uint)ny1 || (uint)k >= (uint)nz1)
            throw new ArgumentOutOfRangeException($"Node coordinates ({i}, {j}, {k}) out of bounds ({nx1}, {ny1}, {nz1}).");

        return i + nx1 * (j + ny1 * k);
    }

    /// <summary>
    /// Computes the degree of freedom index: dof = 3 * node + c, where c in {0, 1, 2} for (Ux, Uy, Uz).
    /// </summary>
    public long Dof(long node, int c)
    {
        if ((uint)c > 2)
            throw new ArgumentOutOfRangeException(nameof(c), "Coordinate component must be 0 (X), 1 (Y), or 2 (Z).");

        return 3L * node + c;
    }

    public int Dof(int node, int c) => (int)Dof((long)node, c);

    /// <summary>
    /// Inverts the element index to (ix, iy, iz).
    /// </summary>
    public void InverseElementIndex(int e, out int rx, out int ry, out int rz)
    {
        int nxNy = Nx * Ny;
        rz = Math.DivRem(e, nxNy, out int rem);
        ry = Math.DivRem(rem, Nx, out rx);
    }

    public void ElementCoords(int e, out int ix, out int iy, out int iz) => InverseElementIndex(e, out ix, out iy, out iz);

    /// <summary>
    /// Inverts the node index to (i, j, k).
    /// </summary>
    public void InverseNodeIndex(long n, out int ri, out int rj, out int rk)
    {
        long nx1 = Nx + 1;
        long nx1Ny1 = nx1 * (Ny + 1);
        long rkLong = Math.DivRem(n, nx1Ny1, out long rem);
        long rjLong = Math.DivRem(rem, nx1, out long riLong);

        rk = (int)rkLong;
        rj = (int)rjLong;
        ri = (int)riLong;
    }

    public void NodeCoords(int n, out int i, out int j, out int k) => InverseNodeIndex(n, out i, out j, out k);

    /// <summary>
    /// Inverts a DOF index to the node index and coordinate component c in {0, 1, 2}.
    /// </summary>
    public void InverseDof(long dof, out long rNode, out int rc)
    {
        rNode = Math.DivRem(dof, 3L, out long rem);
        rc = (int)rem;
    }

    public void NodeFromDof(int dof, out int node, out int c)
    {
        InverseDof(dof, out long rNode, out c);
        node = (int)rNode;
    }

    /// <summary>
    /// Returns the center position in world space (mm) of element (ix, iy, iz).
    /// </summary>
    public Vector3 ElementCenter(int ix, int iy, int iz)
    {
        return Origin + new Vector3((ix + 0.5f) * H, (iy + 0.5f) * H, (iz + 0.5f) * H);
    }

    /// <summary>
    /// Returns the world space position (mm) of node (i, j, k).
    /// </summary>
    public Vector3 NodePosition(int i, int j, int k)
    {
        return Origin + new Vector3(i * H, j * H, k * H);
    }
}

