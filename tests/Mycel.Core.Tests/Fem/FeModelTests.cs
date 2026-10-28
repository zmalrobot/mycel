using System;
using System.Collections.Generic;
using System.Numerics;
using Mycel.Core.Fem;
using Mycel.Core.Voxelization;
using Xunit;

namespace Mycel.Core.Tests.Fem;

public class FeModelTests
{
    [Fact]
    public void FeModel_ActiveNodes_And_ConstrainedDofs_IdentifiedCorrectly()
    {
        // 2x2x2 grid with only 1 active element at (0, 0, 0)
        var grid = new GridSpec(Vector3.Zero, 1.0f, 2, 2, 2);
        byte[] domain = new byte[8];
        domain[0] = 1; // element 0 is solid, elements 1..7 are void

        var mat = new FeMaterial(1000.0, 0.3);
        int[] fixedDofs = { 0, 1, 2 }; // fix node 0 (Ux, Uy, Uz)

        var model = new FeModel(grid, domain, mat, fixedDofs);

        Assert.Equal(1, model.ActiveElementCount);
        Assert.Equal(8, model.ActiveNodeCount); // a single hex8 has 8 active nodes

        // Total nodes in 2x2x2 grid is 3x3x3 = 27
        Assert.Equal(27, model.TotalNodes);
        Assert.Equal(81, model.TotalDofs);

        // Fixed DOFs must be constrained
        Assert.True(model.ConstrainedDofs[0]);
        Assert.True(model.ConstrainedDofs[1]);
        Assert.True(model.ConstrainedDofs[2]);

        // Inactive nodes (e.g. node (2, 2, 2)) must have all 3 DOFs constrained
        int n222 = (int)grid.NodeIndex(2, 2, 2);
        Assert.False(model.ActiveNodes[n222]);
        Assert.True(model.ConstrainedDofs[3 * n222 + 0]);
        Assert.True(model.ConstrainedDofs[3 * n222 + 1]);
        Assert.True(model.ConstrainedDofs[3 * n222 + 2]);

        // Free active node (e.g. node (1, 0, 0)) DOFs should NOT be constrained
        int n100 = (int)grid.NodeIndex(1, 0, 0);
        Assert.True(model.ActiveNodes[n100]);
        Assert.False(model.ConstrainedDofs[3 * n100 + 0]);
        Assert.False(model.ConstrainedDofs[3 * n100 + 1]);
        Assert.False(model.ConstrainedDofs[3 * n100 + 2]);
    }

    [Fact]
    public void FeModel_AssembleForces_ConservesTotalForce_To_1e12()
    {
        var grid = new GridSpec(Vector3.Zero, 2.0f, 4, 4, 4);
        byte[] domain = new byte[64];
        Array.Fill(domain, (byte)1);

        var mat = new FeMaterial(2000.0, 0.3);

        // Create a surface patch on 4 nodes with varying tributary areas
        int[] patchNodes = { 0, 1, 5, 4 };
        double[] areas = { 1.5, 3.0, 3.0, 1.5 };
        var patch = new SurfacePatch(
            Array.Empty<VoxelFace>(),
            patchNodes,
            areas,
            (Vector3.Zero, new Vector3(4, 4, 0)),
            true);

        Vector3 force1 = new(100.0f, -50.0f, 250.0f);
        var loadCase1 = new LoadCase(patch, force1);

        // Second load case on overlapping nodes
        int[] patchNodes2 = { 1, 5, 2, 6 };
        double[] areas2 = { 2.0, 2.0, 2.0, 2.0 };
        var patch2 = new SurfacePatch(
            Array.Empty<VoxelFace>(),
            patchNodes2,
            areas2,
            (Vector3.Zero, new Vector3(4, 4, 0)),
            true);

        Vector3 force2 = new(-30.0f, 80.0f, -100.0f);
        var loadCase2 = new LoadCase(patch2, force2);

        var model = new FeModel(grid, domain, mat, new int[] { 0, 1, 2, 3, 4, 5 }, new[] { loadCase1, loadCase2 });
        double[] f = model.AssembleForces();

        // Sum components over all nodes
        double sumFx = 0.0, sumFy = 0.0, sumFz = 0.0;
        for (int n = 0; n < model.TotalNodes; n++)
        {
            sumFx += f[3 * n + 0];
            sumFy += f[3 * n + 1];
            sumFz += f[3 * n + 2];
        }

        Vector3 expectedTotal = force1 + force2;
        Assert.True(Math.Abs(sumFx - expectedTotal.X) < 1e-12, $"Fx mismatch: {sumFx} vs {expectedTotal.X}");
        Assert.True(Math.Abs(sumFy - expectedTotal.Y) < 1e-12, $"Fy mismatch: {sumFy} vs {expectedTotal.Y}");
        Assert.True(Math.Abs(sumFz - expectedTotal.Z) < 1e-12, $"Fz mismatch: {sumFz} vs {expectedTotal.Z}");
    }

    [Fact]
    public void FeModel_FixDofsHelpers_GenerateExpectedIndices()
    {
        int[] nodes = { 10, 20 };

        // Fix all 3
        int[] dofsAll = FeModel.FixDofs(nodes, true, true, true);
        Assert.Equal(new int[] { 30, 31, 32, 60, 61, 62 }, dofsAll);

        // Fix only X and Z
        int[] dofsXZ = FeModel.FixDofs(nodes, true, false, true);
        Assert.Equal(new int[] { 30, 32, 60, 62 }, dofsXZ);

        // Fix using component list
        int[] dofsZ = FeModel.FixDofs(nodes, stackalloc int[] { 2 });
        Assert.Equal(new int[] { 32, 62 }, dofsZ);

        // Fix SurfacePatch with components params
        var patch = new SurfacePatch(Array.Empty<VoxelFace>(), nodes, new double[] { 1, 1 }, (Vector3.Zero, Vector3.One), true);
        int[] dofsPatch = FeModel.FixDofs(patch, 1, 2);
        Assert.Equal(new int[] { 31, 32, 61, 62 }, dofsPatch);
    }

    [Fact]
    public void FeModel_AssembleForces_EmptyPatchWithForce_ThrowsInvalidOperationException()
    {
        var grid = new GridSpec(Vector3.Zero, 1.0f, 2, 2, 2);
        byte[] domain = new byte[8];
        domain[0] = 1;
        var mat = new FeMaterial(1000.0, 0.3);

        var emptyPatch = new SurfacePatch(
            Array.Empty<VoxelFace>(),
            Array.Empty<int>(),
            Array.Empty<double>(),
            (Vector3.Zero, Vector3.Zero),
            false);

        var loadCase = new LoadCase(emptyPatch, new Vector3(100, 0, 0));
        var model = new FeModel(grid, domain, mat, new int[] { 0, 1, 2, 3, 4, 5 }, new[] { loadCase });

        Assert.Throws<InvalidOperationException>(() => model.AssembleForces());
    }

    [Fact]
    public void FeModel_AssembleForces_EmptyPatchWithZeroForce_Succeeds()
    {
        var grid = new GridSpec(Vector3.Zero, 1.0f, 2, 2, 2);
        byte[] domain = new byte[8];
        domain[0] = 1;
        var mat = new FeMaterial(1000.0, 0.3);

        var emptyPatch = new SurfacePatch(
            Array.Empty<VoxelFace>(),
            Array.Empty<int>(),
            Array.Empty<double>(),
            (Vector3.Zero, Vector3.Zero),
            false);

        var loadCase = new LoadCase(emptyPatch, Vector3.Zero);
        var model = new FeModel(grid, domain, mat, new int[] { 0, 1, 2, 3, 4, 5 }, new[] { loadCase });

        double[] f = model.AssembleForces();
        Assert.All(f, val => Assert.Equal(0.0, val));
    }
}
