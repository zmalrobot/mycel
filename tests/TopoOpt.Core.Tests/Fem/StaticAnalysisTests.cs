using System;
using System.Numerics;
using TopoOpt.Core.Fem;
using TopoOpt.Core.Voxelization;
using Xunit;

namespace TopoOpt.Core.Tests.Fem;

public class StaticAnalysisTests
{
    [Fact]
    public void AverageElementToNode_AveragesSharedElementsCorrectly()
    {
        // 2x2x1 grid, 4 elements
        var grid = new GridSpec(Vector3.Zero, 1.0f, 2, 2, 1);
        byte[] domain = new byte[4];
        Array.Fill(domain, (byte)1);

        var mat = new FeMaterial(1000.0, 0.3);
        var model = new FeModel(grid, domain, mat, new int[] { 0, 1, 2, 3, 4, 5 });

        // Element scalars: 10, 20, 30, 40
        double[] elemScalars = { 10.0, 20.0, 30.0, 40.0 };

        double[] nodalScalars = StaticAnalysis.AverageElementToNode(model, elemScalars);

        // Center node at (1, 1, 0) is shared by all 4 elements
        int centerNode = (int)grid.NodeIndex(1, 1, 0);
        double expectedAvg = (10.0 + 20.0 + 30.0 + 40.0) / 4.0;
        Assert.Equal(expectedAvg, nodalScalars[centerNode], precision: 10);

        // Corner node at (0, 0, 0) is touched only by element 0
        int cornerNode0 = (int)grid.NodeIndex(0, 0, 0);
        Assert.Equal(10.0, nodalScalars[cornerNode0], precision: 10);
    }

    [Fact]
    public void SampleNodalScalar_InterpolatesTrilinearCorrectly()
    {
        // 1x1x1 unit cube
        var grid = new GridSpec(Vector3.Zero, 1.0f, 1, 1, 1);
        byte[] domain = new byte[1] { 1 };

        var mat = new FeMaterial(1000.0, 0.3);
        var model = new FeModel(grid, domain, mat, new int[] { 0, 1, 2, 3, 4, 5 });

        // Linear scalar field: s(x, y, z) = 2*x + 3*y + 5*z
        double[] nodalScalars = new double[8];
        for (int k = 0; k <= 1; k++)
        {
            for (int j = 0; j <= 1; j++)
            {
                for (int i = 0; i <= 1; i++)
                {
                    int n = (int)grid.NodeIndex(i, j, k);
                    nodalScalars[n] = 2.0 * i + 3.0 * j + 5.0 * k;
                }
            }
        }

        // Sample at center (0.5, 0.5, 0.5)
        Vector3 centerPoint = new(0.5f, 0.5f, 0.5f);
        double sampled = StaticAnalysis.SampleNodalScalar(model, nodalScalars, centerPoint);
        double expected = 2.0 * 0.5 + 3.0 * 0.5 + 5.0 * 0.5; // 1 + 1.5 + 2.5 = 5.0
        Assert.Equal(expected, sampled, precision: 10);

        // Sample at (0.25, 0.75, 0.5)
        Vector3 queryPoint = new(0.25f, 0.75f, 0.5f);
        double sampledQ = StaticAnalysis.SampleNodalScalar(model, nodalScalars, queryPoint);
        double expectedQ = 2.0 * 0.25 + 3.0 * 0.75 + 5.0 * 0.5; // 0.5 + 2.25 + 2.5 = 5.25
        Assert.Equal(expectedQ, sampledQ, precision: 10);
    }

    [Fact]
    public void SampleNodalScalar_FallbackNearestNode_WhenOutsideOrInactive()
    {
        var grid = new GridSpec(Vector3.Zero, 1.0f, 1, 1, 1);
        byte[] domain = new byte[1] { 1 };
        var mat = new FeMaterial(1000.0, 0.3);
        var model = new FeModel(grid, domain, mat, new int[] { 0, 1, 2, 3, 4, 5 });

        double[] nodalScalars = new double[8];
        for (int n = 0; n < 8; n++) nodalScalars[n] = n * 10.0;

        // Point outside grid bounds, closest to node (1, 1, 1) which has index 7
        Vector3 outsidePoint = new(5.0f, 5.0f, 5.0f);
        double sampled = StaticAnalysis.SampleNodalScalar(model, nodalScalars, outsidePoint);
        int node111 = (int)grid.NodeIndex(1, 1, 1);
        Assert.Equal(nodalScalars[node111], sampled);
    }

    [Fact]
    public void SampleNodalScalar_InterpolatesUsingOnlyActiveNodes_WhenCellPartiallyActive()
    {
        // 2x1x1 grid: Element 0 is solid, element 1 is void
        var grid = new GridSpec(Vector3.Zero, 1.0f, 2, 1, 1);
        byte[] domain = new byte[2] { 1, 0 };
        var mat = new FeMaterial(1000.0, 0.3);
        var model = new FeModel(grid, domain, mat, new int[] { 0, 1, 2, 3, 4, 5 });

        // Total nodes: 3 x 2 x 2 = 12 nodes
        // Nodes with ix=0 and ix=1 are active; ix=2 are inactive.
        double[] nodalScalars = new double[model.TotalNodes];
        for (int k = 0; k <= 1; k++)
        {
            for (int j = 0; j <= 1; j++)
            {
                for (int i = 0; i <= 2; i++)
                {
                    int n = (int)grid.NodeIndex(i, j, k);
                    nodalScalars[n] = 10.0 * i + 5.0 * j + 2.0 * k;
                }
            }
        }

        // Query at (1.5, 0.5, 0.5), which is in cell 1 (the void cell)
        // In cell 1, only the 4 nodes at x=1 are active.
        Vector3 queryPoint = new(1.5f, 0.5f, 0.5f);
        double sampled = StaticAnalysis.SampleNodalScalar(model, nodalScalars, queryPoint);

        // At x=1, y=0.5, z=0.5, the interpolated scalar from the 4 nodes at x=1 is:
        // 10*1 + 5*0.5 + 2*0.5 = 10 + 2.5 + 1.0 = 13.5
        Assert.Equal(13.5, sampled, precision: 10);
    }
}
