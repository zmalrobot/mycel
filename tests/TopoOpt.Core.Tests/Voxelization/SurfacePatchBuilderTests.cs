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

    [Fact]
    [Trait("Category", "Example")]
    public void Build_AssemblaggioPins_MapsNonZeroNodesAndPassesValidation()
    {
        string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../examples/obj_1_Assemblaggio.stl"));
        if (!File.Exists(path))
            path = Path.GetFullPath("examples/obj_1_Assemblaggio.stl");
        Assert.True(File.Exists(path), $"STL file not found at: {path}");

        using var stream = File.OpenRead(path);
        var readResult = StlReader.Read(stream);
        var mesh = readResult.Mesh;

        // Voxelise at Standard preset (target 100k solid elements) with keepOnlyLargestComponent: true
        float h = TopoOpt.Core.Workflow.ResolutionConfig.ComputeVoxelSize(mesh, 100_000);
        var grid = GridSpec.FromVoxelSize(mesh, h, padding: 1);
        var (model, _) = Voxeliser.Voxelise(mesh, grid, keepOnlyLargestComponent: true);
        Assert.False(model.Warnings.Any(w => w.Contains("disconnected components")), string.Join("; ", model.Warnings));

        // Find pin triangles using RegionSelector
        var pos = mesh.Positions.Span;
        var indices = mesh.Indices.Span;
        RegionSelector selector = new(mesh);

        // Pin extends furthest along Y
        int seedTri = 0;
        float maxY = float.MinValue;
        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            float cy = (pos[indices[i * 3]].Y + pos[indices[i * 3 + 1]].Y + pos[indices[i * 3 + 2]].Y) / 3f;
            if (cy > maxY)
            {
                maxY = cy;
                seedTri = i;
            }
        }

        int[] pinTriangles = selector.GrowRegion(seedTri, 30f);
        Assert.NotEmpty(pinTriangles);

        // Build surface patch for the pin
        var patch = SurfacePatchBuilder.Build(model, mesh, pinTriangles);

        // Verify patch has mapped FE nodes and faces
        Assert.True(patch.NodeCount > 0, $"Expected patch.NodeCount > 0, got {patch.NodeCount}");
        Assert.True(patch.Faces.Count > 0, $"Expected patch.Faces.Count > 0, got {patch.Faces.Count}");

        // Validate patches with ModelValidator
        Guid loadId = Guid.NewGuid();
        Guid supportId = Guid.NewGuid();

        // Select a support patch on the opposite side (min Y)
        int supportSeedTri = 0;
        float minY = float.MaxValue;
        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            float cy = (pos[indices[i * 3]].Y + pos[indices[i * 3 + 1]].Y + pos[indices[i * 3 + 2]].Y) / 3f;
            if (cy < minY)
            {
                minY = cy;
                supportSeedTri = i;
            }
        }
        int[] supportTriangles = selector.GrowRegion(supportSeedTri, 30f);
        var supportPatch = SurfacePatchBuilder.Build(model, mesh, supportTriangles);
        Assert.True(supportPatch.NodeCount > 0);

        var validationIssues = ModelValidator.ValidatePatches(model, mesh, [
            (loadId, SelectionKind.Load, patch),
            (supportId, SelectionKind.Support, supportPatch)
        ]);

        Assert.DoesNotContain(validationIssues, issue => issue.Code == ModelValidator.ErrEmptyPatch);

        // Also test selecting the lateral cylindrical surface of the pin (normal.Y near 0, but Y in upper half)
        int lateralSeed = -1;
        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            float cy = (pos[indices[i * 3]].Y + pos[indices[i * 3 + 1]].Y + pos[indices[i * 3 + 2]].Y) / 3f;
            Vector3 n = mesh.TriangleNormals.Span[i];
            if (cy > 10f && MathF.Abs(n.Y) < 0.2f)
            {
                lateralSeed = i;
                break;
            }
        }
        Assert.True(lateralSeed >= 0, "Lateral seed triangle not found");
        int[] lateralTriangles = selector.GrowRegion(lateralSeed, 30f);
        Assert.NotEmpty(lateralTriangles);
        var lateralPatch = SurfacePatchBuilder.Build(model, mesh, lateralTriangles);
        Assert.True(lateralPatch.NodeCount > 0, $"Lateral patch should map nodes, got {lateralPatch.NodeCount}");

        // Verify that the overlap zone Y in [92, 95] contains solid voxels (no hollow cavity at pin roots)
        int solidOverlapCount = 0;
        for (int iz = 0; iz < model.Grid.Nz; iz++)
        {
            for (int iy = 0; iy < model.Grid.Ny; iy++)
            {
                for (int ix = 0; ix < model.Grid.Nx; ix++)
                {
                    Vector3 center = model.Grid.ElementCenter(ix, iy, iz);
                    if (center.Y >= 92f && center.Y <= 95f && model.Domain[model.Grid.ElementIndex(ix, iy, iz)] == 1)
                    {
                        solidOverlapCount++;
                    }
                }
            }
        }
        Assert.True(solidOverlapCount > 0, "Overlap region at pin roots must be solid under non-zero winding rule.");
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

