using System;
using System.Collections.Generic;
using System.Numerics;
using TopoOpt.Core.Voxelization;

namespace TopoOpt.Core.Fem;

/// <summary>
/// Finite element model discretised on a structured Hex8 grid.
/// </summary>
public sealed class FeModel
{
    public GridSpec Grid { get; }
    public byte[] Domain { get; }
    public FeMaterial Material { get; }
    public double[] ElementDensity { get; }
    public double Penal { get; set; } = 3.0;
    public int[] FixedDofs { get; }
    public List<LoadCase> Loads { get; }

    public int TotalElements => Grid.Nx * Grid.Ny * Grid.Nz;
    public int TotalNodes => (Grid.Nx + 1) * (Grid.Ny + 1) * (Grid.Nz + 1);
    public int TotalDofs => 3 * TotalNodes;

    public int ActiveElementCount { get; }
    public int ActiveNodeCount { get; }
    public int[] ActiveElements { get; }
    public bool[] ActiveNodes { get; }

    /// <summary>
    /// Boolean mask of constrained DOFs (Fixed DOFs + all DOFs of Inactive nodes).
    /// Constrained DOFs receive an identity row in the system operator A = P K P + (I - P).
    /// </summary>
    public bool[] ConstrainedDofs { get; }

    /// <summary>
    /// Active elements grouped by 8-colour lattice index: c = (ix &amp; 1) | ((iy &amp; 1) &lt;&lt; 1) | ((iz &amp; 1) &lt;&lt; 2).
    /// No two elements of the same colour share any node.
    /// </summary>
    public int[][] ElementsByColor { get; }

    public FeModel(
        GridSpec grid,
        byte[] domain,
        FeMaterial material,
        int[] fixedDofs,
        IEnumerable<LoadCase>? loads = null,
        double[]? elementDensity = null,
        double penal = 3.0)
    {
        Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        Domain = domain ?? throw new ArgumentNullException(nameof(domain));
        Material = material ?? throw new ArgumentNullException(nameof(material));
        FixedDofs = fixedDofs ?? Array.Empty<int>();
        Penal = penal;

        if (Domain.Length != TotalElements)
        {
            throw new ArgumentException(
                $"Domain array length ({Domain.Length}) does not match Grid TotalElements ({TotalElements}).",
                nameof(domain));
        }

        Loads = loads != null ? new List<LoadCase>(loads) : new List<LoadCase>();

        // Initialize element densities (default 1.0)
        if (elementDensity != null)
        {
            if (elementDensity.Length != TotalElements)
                throw new ArgumentException($"ElementDensity length ({elementDensity.Length}) must match TotalElements ({TotalElements}).");
            ElementDensity = (double[])elementDensity.Clone();
        }
        else
        {
            ElementDensity = new double[TotalElements];
            Array.Fill(ElementDensity, 1.0);
        }

        // Identify active elements and 8-colouring
        List<int> activeElemList = new();
        List<int>[] colorLists = new List<int>[8];
        for (int c = 0; c < 8; c++)
            colorLists[c] = new List<int>();

        ActiveNodes = new bool[TotalNodes];

        int nx = Grid.Nx;
        int ny = Grid.Ny;
        int nz = Grid.Nz;
        int nx1 = nx + 1;
        int ny1 = ny + 1;

        for (int iz = 0; iz < nz; iz++)
        {
            int cz = (iz & 1) << 2;
            for (int iy = 0; iy < ny; iy++)
            {
                int cyz = cz | ((iy & 1) << 1);
                for (int ix = 0; ix < nx; ix++)
                {
                    int e = ix + nx * (iy + ny * iz);
                    if (Domain[e] != 0)
                    {
                        activeElemList.Add(e);
                        int color = cyz | (ix & 1);
                        colorLists[color].Add(e);

                        // Mark its 8 corner nodes as active
                        // Node order:
                        // 0: (ix, iy, iz), 1: (ix+1, iy, iz), 2: (ix+1, iy+1, iz), 3: (ix, iy+1, iz)
                        // 4: (ix, iy, iz+1), 5: (ix+1, iy, iz+1), 6: (ix+1, iy+1, iz+1), 7: (ix, iy+1, iz+1)
                        int n0 = ix + nx1 * (iy + ny1 * iz);
                        int n1 = n0 + 1;
                        int n3 = n0 + nx1;
                        int n2 = n3 + 1;
                        int n4 = n0 + nx1 * ny1;
                        int n5 = n4 + 1;
                        int n7 = n4 + nx1;
                        int n6 = n7 + 1;

                        ActiveNodes[n0] = true;
                        ActiveNodes[n1] = true;
                        ActiveNodes[n2] = true;
                        ActiveNodes[n3] = true;
                        ActiveNodes[n4] = true;
                        ActiveNodes[n5] = true;
                        ActiveNodes[n6] = true;
                        ActiveNodes[n7] = true;
                    }
                }
            }
        }

        ActiveElements = activeElemList.ToArray();
        ActiveElementCount = ActiveElements.Length;

        ElementsByColor = new int[8][];
        for (int c = 0; c < 8; c++)
            ElementsByColor[c] = colorLists[c].ToArray();

        int activeNodeCount = 0;
        for (int n = 0; n < TotalNodes; n++)
        {
            if (ActiveNodes[n]) activeNodeCount++;
        }
        ActiveNodeCount = activeNodeCount;

        // Build Constrained DOFs mask (Fixed DOFs + inactive node DOFs)
        ConstrainedDofs = new bool[TotalDofs];

        // 1. All DOFs of inactive nodes are constrained
        for (int n = 0; n < TotalNodes; n++)
        {
            if (!ActiveNodes[n])
            {
                int dof0 = 3 * n;
                ConstrainedDofs[dof0 + 0] = true;
                ConstrainedDofs[dof0 + 1] = true;
                ConstrainedDofs[dof0 + 2] = true;
            }
        }

        // 2. Fixed DOFs are constrained
        for (int i = 0; i < FixedDofs.Length; i++)
        {
            int dof = FixedDofs[i];
            if ((uint)dof < (uint)TotalDofs)
            {
                ConstrainedDofs[dof] = true;
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(fixedDofs), $"Fixed DOF {dof} is out of bounds [0, {TotalDofs - 1}].");
            }
        }
    }

    /// <summary>
    /// Helper to generate fixed DOF indices for a set of nodes and coordinate components (0=X, 1=Y, 2=Z).
    /// </summary>
    public static int[] FixDofs(IEnumerable<int> nodes, bool fixX = true, bool fixY = true, bool fixZ = true)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        List<int> dofs = new();
        foreach (int node in nodes)
        {
            if (fixX) dofs.Add(3 * node + 0);
            if (fixY) dofs.Add(3 * node + 1);
            if (fixZ) dofs.Add(3 * node + 2);
        }
        return dofs.ToArray();
    }

    /// <summary>
    /// Helper to generate fixed DOF indices for a surface patch.
    /// </summary>
    public static int[] FixDofs(SurfacePatch patch, bool fixX = true, bool fixY = true, bool fixZ = true)
    {
        ArgumentNullException.ThrowIfNull(patch);
        return FixDofs(patch.Nodes, fixX, fixY, fixZ);
    }

    /// <summary>
    /// Helper to generate fixed DOF indices for nodes with specific coordinate components.
    /// </summary>
    public static int[] FixDofs(IEnumerable<int> nodes, ReadOnlySpan<int> components)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        List<int> dofs = new();
        foreach (int node in nodes)
        {
            for (int c = 0; c < components.Length; c++)
            {
                int comp = components[c];
                if ((uint)comp > 2)
                    throw new ArgumentOutOfRangeException(nameof(components), "Component must be 0 (X), 1 (Y), or 2 (Z).");
                dofs.Add(3 * node + comp);
            }
        }
        return dofs.ToArray();
    }

    /// <summary>
    /// Assembles consistent nodal forces from all load cases into a global vector of length TotalDofs.
    /// For each load case: F_node = TotalForce * NodeArea[node] / Σ NodeArea.
    /// Guarantees that Σ over nodes equals TotalForce within 1e-12 relative.
    /// </summary>
    public double[] AssembleForces()
    {
        double[] f = new double[TotalDofs];

        foreach (var load in Loads)
        {
            var patch = load.Patch;
            if (patch.NodeCount == 0) continue;

            double totalArea = 0.0;
            for (int i = 0; i < patch.NodeCount; i++)
            {
                totalArea += patch.NodeArea[i];
            }

            if (totalArea <= 0.0)
            {
                // Fallback: distribute equally if tributary areas are zero
                double invCount = 1.0 / patch.NodeCount;
                double fx = load.TotalForce.X * invCount;
                double fy = load.TotalForce.Y * invCount;
                double fz = load.TotalForce.Z * invCount;

                for (int i = 0; i < patch.NodeCount; i++)
                {
                    int node = patch.Nodes[i];
                    f[3 * node + 0] += fx;
                    f[3 * node + 1] += fy;
                    f[3 * node + 2] += fz;
                }
            }
            else
            {
                double invTotalArea = 1.0 / totalArea;
                for (int i = 0; i < patch.NodeCount; i++)
                {
                    int node = patch.Nodes[i];
                    double weight = patch.NodeArea[i] * invTotalArea;
                    f[3 * node + 0] += load.TotalForce.X * weight;
                    f[3 * node + 1] += load.TotalForce.Y * weight;
                    f[3 * node + 2] += load.TotalForce.Z * weight;
                }
            }
        }

        return f;
    }
}
