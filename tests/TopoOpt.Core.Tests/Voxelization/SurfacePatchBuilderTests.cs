using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using TopoOpt.Core.Geometry;
using TopoOpt.Core.Tests.Fixtures;
using TopoOpt.Core.Voxelization;
using Xunit;

namespace TopoOpt.Core.Tests.Voxelization;

public sealed class SurfacePatchBuilderTests
{
    [Fact]
    public void Build_BoxTopFace_ProducesExactFaceCountAndTributaryNodeAreas()
    {
        // 4 x 5 x 6 box with h = 1.0 mm -> top face (+Z) has 4 x 5 = 20 voxel faces
        Vector3 size = new(4f, 5f, 6f);
        IndexedMesh box = CreateMesh(StlFixtures.Box(size));

        GridSpec grid = GridSpec.FromVoxelSize(box, 1.0f, padding: 0);
        var (model, _) = Voxeliser.Voxelise(box, grid);

        // Find the 2 triangles of the top face (+Z normal)
        List<int> topTris = new();
        for (int i = 0; i < box.TriangleCount; i++)
        {
            if (box.TriangleNormals.Span[i].Z > 0.99f)
            {
                topTris.Add(i);
            }
        }
        Assert.Equal(2, topTris.Count);

        SurfacePatch patch = SurfacePatchBuilder.Build(model, box, topTris.ToArray(), tau: 1.0, minNormalDot: 0.5);

        int expectedFaces = grid.Nx * grid.Ny; // 4 * 5 = 20
        int expectedNodes = (grid.Nx + 1) * (grid.Ny + 1); // 5 * 6 = 30
        double hSq = grid.H * grid.H; // 1.0 mm^2

        Assert.Equal(expectedFaces, patch.FaceCount);
        Assert.Equal(expectedNodes, patch.NodeCount);

        // Verify total area identity: sum(NodeArea) == Faces.Length * h^2
        double totalNodeArea = patch.NodeArea.Sum();
        double expectedTotalArea = expectedFaces * hSq;
        Assert.Equal(expectedTotalArea, totalNodeArea, 1e-4);

        // Check corner, edge, and interior tributary areas
        // Top face nodes all have k = grid.Nz
        int cornerCount = 0, edgeCount = 0, interiorCount = 0;
        for (int idx = 0; idx < patch.Nodes.Length; idx++)
        {
            long n = patch.Nodes[idx];
            grid.InverseNodeIndex(n, out int i, out int j, out int k);
            Assert.Equal(grid.Nz, k);

            double area = patch.NodeArea[idx];
            bool isXBorder = (i == 0 || i == grid.Nx);
            bool isYBorder = (j == 0 || j == grid.Ny);

            if (isXBorder && isYBorder)
            {
                // Corner node touches 1 face -> h^2 / 4
                Assert.Equal(hSq * 0.25, area, 1e-4);
                cornerCount++;
            }
            else if (isXBorder || isYBorder)
            {
                // Edge node touches 2 faces -> h^2 / 2
                Assert.Equal(hSq * 0.50, area, 1e-4);
                edgeCount++;
            }
            else
            {
                // Interior node touches 4 faces -> h^2
                Assert.Equal(hSq * 1.00, area, 1e-4);
                interiorCount++;
            }
        }

        Assert.Equal(4, cornerCount);
        Assert.Equal(2 * (grid.Nx - 1) + 2 * (grid.Ny - 1), edgeCount);
        Assert.Equal((grid.Nx - 1) * (grid.Ny - 1), interiorCount);
    }

    [Fact]
    public void Build_CylindricalHoleInnerWall_FacesOnlyOnInnerWallAndNotOnOuterOrCaps()
    {
        float rOut = 15f;
        float rIn = 6f;
        float h = 0.5f;
        int n = 32;

        IndexedMesh hollowCyl = CreateMesh(StlFixtures.Cylinder(rOut, 20f, n, rIn));
        GridSpec grid = GridSpec.FromVoxelSize(hollowCyl, h, padding: 1);
        var (model, _) = Voxeliser.Voxelise(hollowCyl, grid);

        // Select the inner wall triangles using RegionSelector
        RegionSelector selector = new(hollowCyl);
        int innerSeed = -1;
        ReadOnlySpan<Vector3> pos = hollowCyl.Positions.Span;
        ReadOnlySpan<int> indices = hollowCyl.Indices.Span;

        for (int i = 0; i < hollowCyl.TriangleCount; i++)
        {
            Vector3 norm = hollowCyl.TriangleNormals.Span[i];
            if (MathF.Abs(norm.Z) < 0.01f)
            {
                Vector3 c = (pos[indices[i * 3]] + pos[indices[i * 3 + 1]] + pos[indices[i * 3 + 2]]) / 3f;
                float rXY = MathF.Sqrt(c.X * c.X + c.Y * c.Y);
                if (rXY < (rIn + rOut) * 0.5f)
                {
                    innerSeed = i;
                    break;
                }
            }
        }
        Assert.True(innerSeed >= 0);

        int[] innerTris = selector.GrowRegion(innerSeed, 30f);
        Assert.Equal(n * 2, innerTris.Length);

        SurfacePatch patch = SurfacePatchBuilder.Build(model, hollowCyl, innerTris, tau: 1.0, minNormalDot: 0.5);

        Assert.True(patch.FaceCount > 0);
        Assert.True(patch.NodeCount > 0);

        // Every face in the patch must have its normal pointing inward towards Z-axis (dot with radius vector < 0)
        // and its radius must be close to rIn (within 1.5 * h), and Z normal must be near 0
        foreach (VoxelFace f in patch.Faces)
        {
            Assert.Equal(0f, f.Normal.Z, 1e-4);
            float rFace = MathF.Sqrt(f.Center.X * f.Center.X + f.Center.Y * f.Center.Y);
            Assert.True(MathF.Abs(rFace - rIn) <= 1.5f * h,
                $"Face center radius {rFace} deviates from rIn {rIn}.");

            Vector2 radial = Vector2.Normalize(new Vector2(f.Center.X, f.Center.Y));
            Vector2 faceNorm2D = new(f.Normal.X, f.Normal.Y);
            Assert.True(Vector2.Dot(radial, faceNorm2D) < -0.1f,
                "Inner wall voxel face normal must point inwards towards the center axis.");
        }
    }

    [Fact]
    public void Build_EmptyOrSubVoxelSelection_ProducesEmptyPatch()
    {
        IndexedMesh box = CreateMesh(StlFixtures.Box(new Vector3(50f, 50f, 50f)));
        GridSpec grid = GridSpec.FromVoxelSize(box, 10.0f, padding: 0);
        var (model, _) = Voxeliser.Voxelise(box, grid);

        SurfacePatch emptyPatch = SurfacePatchBuilder.Build(model, box, [], tau: 1.0, minNormalDot: 0.5);

        Assert.Equal(0, emptyPatch.FaceCount);
        Assert.Equal(0, emptyPatch.NodeCount);
        Assert.Empty(patchNodes(emptyPatch));
    }

    private static int[] patchNodes(SurfacePatch p) => p.Nodes;

    private static IndexedMesh CreateMesh(List<Tri> tris)
    {
        using MemoryStream ms = new();
        StlFixtures.WriteBinary(ms, tris);
        ms.Position = 0;
        return StlReader.Read(ms).Mesh;
    }
}

