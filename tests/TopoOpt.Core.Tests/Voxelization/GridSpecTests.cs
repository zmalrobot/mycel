using System;
using System.Numerics;
using TopoOpt.Core.Geometry;
using TopoOpt.Core.Tests.Fixtures;
using TopoOpt.Core.Voxelization;
using Xunit;

namespace TopoOpt.Core.Tests.Voxelization;

public sealed class GridSpecTests
{
    [Fact]
    public void Indexing_Element_RoundTrip_MatchesAgentsFormula()
    {
        GridSpec grid = new(Vector3.Zero, 1.0f, 10, 20, 30);

        Assert.Equal(6000, grid.TotalElements);

        for (int iz = 0; iz < grid.Nz; iz += 3)
        {
            for (int iy = 0; iy < grid.Ny; iy += 4)
            {
                for (int ix = 0; ix < grid.Nx; ix += 2)
                {
                    int expected = ix + grid.Nx * (iy + grid.Ny * iz);
                    int actual = grid.ElementIndex(ix, iy, iz);
                    Assert.Equal(expected, actual);

                    grid.InverseElementIndex(actual, out int rx, out int ry, out int rz);
                    Assert.Equal(ix, rx);
                    Assert.Equal(iy, ry);
                    Assert.Equal(iz, rz);
                }
            }
        }
    }

    [Fact]
    public void Indexing_NodeAndDof_RoundTrip_MatchesAgentsFormula()
    {
        GridSpec grid = new(new Vector3(5f, -10f, 2f), 0.5f, 15, 25, 35);

        for (int k = 0; k <= grid.Nz; k += 5)
        {
            for (int j = 0; j <= grid.Ny; j += 5)
            {
                for (int i = 0; i <= grid.Nx; i += 3)
                {
                    long expectedNode = i + (long)(grid.Nx + 1) * (j + (long)(grid.Ny + 1) * k);
                    long actualNode = grid.NodeIndex(i, j, k);
                    Assert.Equal(expectedNode, actualNode);

                    grid.InverseNodeIndex(actualNode, out int ri, out int rj, out int rk);
                    Assert.Equal(i, ri);
                    Assert.Equal(j, rj);
                    Assert.Equal(k, rk);

                    for (int c = 0; c < 3; c++)
                    {
                        long dof = grid.Dof(actualNode, c);
                        Assert.Equal(3L * actualNode + c, dof);

                        grid.InverseDof(dof, out long rNode, out int rc);
                        Assert.Equal(actualNode, rNode);
                        Assert.Equal(c, rc);
                    }
                }
            }
        }
    }

    [Fact]
    public void Indexing_LargeGrid_HandlesLongNodeAndDofOverflowCorrectly()
    {
        // Grid with > 2 billion DOFs (e.g. 1000 x 1000 x 1000 -> 10^9 nodes -> 3*10^9 DOFs)
        GridSpec grid = new(Vector3.Zero, 1.0f, 1000, 1000, 1000);

        long maxNode = grid.NodeIndex(1000, 1000, 1000);
        long maxDof = grid.Dof(maxNode, 2);

        Assert.True(maxDof > int.MaxValue, "Max DOF must exceed 32-bit integer range.");

        grid.InverseDof(maxDof, out long rNode, out int rc);
        Assert.Equal(maxNode, rNode);
        Assert.Equal(2, rc);

        grid.InverseNodeIndex(rNode, out int ri, out int rj, out int rk);
        Assert.Equal(1000, ri);
        Assert.Equal(1000, rj);
        Assert.Equal(1000, rk);
    }

    [Fact]
    public void FromVoxelSize_WithMesh_CalculatesCorrectExtentsAndPadding()
    {
        IndexedMesh box = CreateMesh(StlFixtures.Box(new Vector3(10f, 20f, 30f)));
        GridSpec grid = GridSpec.FromVoxelSize(box, 1.0f, padding: 1);

        Assert.Equal(12, grid.Nx);
        Assert.Equal(22, grid.Ny);
        Assert.Equal(32, grid.Nz);
        Assert.Equal(1.0f, grid.H);
        Assert.Equal(-6.0f, grid.Origin.X, 1e-4);
        Assert.Equal(-11.0f, grid.Origin.Y, 1e-4);
        Assert.Equal(-16.0f, grid.Origin.Z, 1e-4);
    }

    [Fact]
    public void FromVoxelSize_ExceedingMaxElementsCap_ThrowsInvalidOperationException()
    {
        IndexedMesh box = CreateMesh(StlFixtures.Box(new Vector3(100f, 100f, 100f)));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            GridSpec.FromVoxelSize(box, 0.1f, padding: 0, maxElements: 8_000_000));

        Assert.Contains("exceeds configurable cap", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromTargetElementCount_CalculatesPitchAccurately()
    {
        IndexedMesh box = CreateMesh(StlFixtures.Box(new Vector3(20f, 20f, 20f))); // Vol = 8000 mm^3
        int targetElements = 8000; // -> h = cbrt(8000 / 8000) = 1.0 mm

        GridSpec grid = GridSpec.FromTargetElementCount(box, targetElements, padding: 0);

        Assert.Equal(1.0f, grid.H, 1e-3f);
        Assert.Equal(20, grid.Nx);
        Assert.Equal(20, grid.Ny);
        Assert.Equal(20, grid.Nz);
    }

    private static IndexedMesh CreateMesh(System.Collections.Generic.List<Tri> tris)
    {
        using System.IO.MemoryStream ms = new();
        StlFixtures.WriteBinary(ms, tris);
        ms.Position = 0;
        return StlReader.Read(ms).Mesh;
    }
}

