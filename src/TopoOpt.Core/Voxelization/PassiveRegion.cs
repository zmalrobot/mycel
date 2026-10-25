using System;
using System.Collections.Generic;

namespace TopoOpt.Core.Voxelization;

public static class PassiveRegion
{
    /// <summary>
    /// Builds a solid passive mask (byte[] of length TotalElements where 1 = permanently solid)
    /// for elements within a Chebyshev distance of radiusInVoxels from the specified patch nodes.
    /// Elements already in initialDomain will have their passive status set to 1.
    /// </summary>
    public static byte[] Build(
        GridSpec grid,
        byte[]? initialDomain,
        IEnumerable<int> patchNodes,
        int radiusInVoxels = 2)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(patchNodes);

        int totalElements = (int)grid.TotalElements;
        byte[] passiveMask = new byte[totalElements];

        if (initialDomain is not null)
        {
            Array.Copy(initialDomain, passiveMask, Math.Min(initialDomain.Length, totalElements));
        }

        int nx = grid.Nx;
        int ny = grid.Ny;
        int nz = grid.Nz;

        foreach (int node in patchNodes)
        {
            grid.NodeCoords(node, out int ni, out int nj, out int nk);

            int minIx = Math.Clamp(ni - radiusInVoxels, 0, nx - 1);
            int maxIx = Math.Clamp(ni + radiusInVoxels - 1, 0, nx - 1);

            int minIy = Math.Clamp(nj - radiusInVoxels, 0, ny - 1);
            int maxIy = Math.Clamp(nj + radiusInVoxels - 1, 0, ny - 1);

            int minIz = Math.Clamp(nk - radiusInVoxels, 0, nz - 1);
            int maxIz = Math.Clamp(nk + radiusInVoxels - 1, 0, nz - 1);

            for (int iz = minIz; iz <= maxIz; iz++)
            {
                int baseRow = nx * (0 + ny * iz);
                for (int iy = minIy; iy <= maxIy; iy++)
                {
                    int elemRow = nx * (iy + ny * iz);
                    for (int ix = minIx; ix <= maxIx; ix++)
                    {
                        passiveMask[elemRow + ix] = 1;
                    }
                }
            }
        }

        return passiveMask;
    }
}

