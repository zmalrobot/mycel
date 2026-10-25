using System;
using System.Collections.Generic;
using System.Numerics;
using TopoOpt.Core.Geometry;
using TopoOpt.Core.Tests.Fixtures;
using TopoOpt.Core.Voxelization;
using Xunit;

namespace TopoOpt.Core.Tests.Voxelization;

public class PassiveRegionTests
{
    [Fact]
    public void Build_MarksElementsWithinChebyshevRadiusAsSolid()
    {
        // 10x10x10 grid with h = 1.0
        var grid = new GridSpec(Vector3.Zero, 1.0f, 10, 10, 10);
        var domain = new byte[10 * 10 * 10]; // all 0 initially

        // Suppose node at (5, 5, 5) is a patch node
        int patchNode = (int)grid.NodeIndex(5, 5, 5);

        // Radius r = 2 voxels (chebyshev distance <= 2 in grid units)
        var passiveDomain = PassiveRegion.Build(grid, domain, new[] { patchNode }, radiusInVoxels: 2);

        // All elements whose Chebyshev distance to node (5,5,5) is <= 2
        // A node (i,j,k) touches 8 elements with corners at (i,j,k), i.e., element coords in [i-1, i] x [j-1, j] x [k-1, k].
        // For Chebyshev radius 2 around node (5,5,5):
        // Elements with ix in [5 - 2, 5 + 1] = [3, 6], iy in [3, 6], iz in [3, 6] should be 1.
        for (int iz = 0; iz < 10; iz++)
        {
            for (int iy = 0; iy < 10; iy++)
            {
                for (int ix = 0; ix < 10; ix++)
                {
                    int elem = grid.ElementIndex(ix, iy, iz);
                    bool inBox = (ix >= 3 && ix <= 6) && (iy >= 3 && iy <= 6) && (iz >= 3 && iz <= 6);
                    if (inBox)
                    {
                        Assert.Equal(1, passiveDomain[elem]);
                    }
                    else
                    {
                        Assert.Equal(0, passiveDomain[elem]);
                    }
                }
            }
        }
    }
}
