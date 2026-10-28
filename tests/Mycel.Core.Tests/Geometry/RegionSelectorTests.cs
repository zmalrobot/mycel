using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Mycel.Core.Geometry;
using Mycel.Core.Tests.Fixtures;
using Xunit;

namespace Mycel.Core.Tests.Geometry;

public sealed class RegionSelectorTests
{
    [Fact]
    public void GrowRegion_OnBoxFace_SelectsExactlyTwoTriangles()
    {
        IndexedMesh box = LoadMesh(StlFixtures.Box(new Vector3(20f, 20f, 20f)));
        RegionSelector selector = new(box);

        for (int seed = 0; seed < box.TriangleCount; seed++)
        {
            int[] region = selector.GrowRegion(seed, maxDihedralAngleDegrees: 30f);
            Assert.Equal(2, region.Length);

            // Both selected triangles must have the exact same face normal
            Vector3 n0 = box.TriangleNormals.Span[region[0]];
            Vector3 n1 = box.TriangleNormals.Span[region[1]];
            Assert.True(Vector3.Dot(n0, n1) > 0.999f);
        }
    }

    [Fact]
    public void GrowRegion_OnFinelyTessellatedCylinderLateralWall_SelectsAllLateralTrianglesAndNoCaps()
    {
        int segments = 64;
        IndexedMesh cyl = LoadMesh(StlFixtures.Cylinder(10f, 30f, segments, holeRadius: 0f));
        RegionSelector selector = new(cyl);

        // Find a lateral triangle (normal Z component near 0)
        int lateralSeed = -1;
        int topCapSeed = -1;
        for (int i = 0; i < cyl.TriangleCount; i++)
        {
            Vector3 n = cyl.TriangleNormals.Span[i];
            if (MathF.Abs(n.Z) < 0.01f && lateralSeed < 0)
            {
                lateralSeed = i;
            }
            else if (n.Z > 0.99f && topCapSeed < 0)
            {
                topCapSeed = i;
            }
        }

        Assert.True(lateralSeed >= 0);
        Assert.True(topCapSeed >= 0);

        int[] lateralRegion = selector.GrowRegion(lateralSeed, maxDihedralAngleDegrees: 30f);
        Assert.Equal(segments * 2, lateralRegion.Length);
        foreach (int idx in lateralRegion)
        {
            Assert.True(MathF.Abs(cyl.TriangleNormals.Span[idx].Z) < 0.01f);
        }

        int[] topCapRegion = selector.GrowRegion(topCapSeed, maxDihedralAngleDegrees: 30f);
        Assert.Equal(segments, topCapRegion.Length);
        foreach (int idx in topCapRegion)
        {
            Assert.True(cyl.TriangleNormals.Span[idx].Z > 0.99f);
        }
    }

    [Fact]
    public void GrowRegion_OnHollowCylinderInnerHole_SelectsOnlyInnerWall()
    {
        int segments = 48;
        float rOut = 15f;
        float rIn = 5f;
        IndexedMesh hollowCyl = LoadMesh(StlFixtures.Cylinder(rOut, 20f, segments, holeRadius: rIn));
        RegionSelector selector = new(hollowCyl);

        // Find an inner-wall triangle (normal Z near 0, centroid radius near rIn)
        int innerSeed = -1;
        ReadOnlySpan<Vector3> pos = hollowCyl.Positions.Span;
        ReadOnlySpan<int> indices = hollowCyl.Indices.Span;

        for (int i = 0; i < hollowCyl.TriangleCount; i++)
        {
            Vector3 n = hollowCyl.TriangleNormals.Span[i];
            if (MathF.Abs(n.Z) < 0.01f)
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

        int[] innerHoleRegion = selector.GrowRegion(innerSeed, maxDihedralAngleDegrees: 30f);
        Assert.Equal(segments * 2, innerHoleRegion.Length);
    }

    [Fact]
    public void GrowRegion_FlatFace_OnCylinderLateralWall_SelectsNarrowStrip()
    {
        int segments = 64;
        IndexedMesh cyl = LoadMesh(StlFixtures.Cylinder(10f, 30f, segments, holeRadius: 0f));
        RegionSelector selector = new(cyl);

        int lateralSeed = -1;
        for (int i = 0; i < cyl.TriangleCount; i++)
        {
            if (MathF.Abs(cyl.TriangleNormals.Span[i].Z) < 0.01f)
            {
                lateralSeed = i;
                break;
            }
        }
        Assert.True(lateralSeed >= 0);

        // FlatFace mode with 10 degree tolerance: should select only faces whose normal is within 10 degrees of seed
        // Each segment is 360 / 64 = 5.625 degrees, so at most ~3 segments (6 triangles) can be selected, NOT all 128!
        int[] flatRegion = selector.GrowRegion(lateralSeed, SelectionMode.FlatFace, toleranceDegrees: 10f);
        Assert.True(flatRegion.Length <= 6, $"Expected narrow strip (<= 6 triangles), got {flatRegion.Length}");
        Assert.True(flatRegion.Length >= 2);

        // While SmoothSurface without reach selects all 128
        int[] smoothAll = selector.GrowRegion(lateralSeed, SelectionMode.SmoothSurface, toleranceDegrees: 30f);
        Assert.Equal(segments * 2, smoothAll.Length);
    }

    [Fact]
    public void GrowRegion_SmoothSurface_WithReachLimit_ConstrainsSelection()
    {
        int segments = 64;
        IndexedMesh cyl = LoadMesh(StlFixtures.Cylinder(10f, 10f, segments, holeRadius: 0f));
        RegionSelector selector = new(cyl);

        int lateralSeed = -1;
        for (int i = 0; i < cyl.TriangleCount; i++)
        {
            if (MathF.Abs(cyl.TriangleNormals.Span[i].Z) < 0.01f)
            {
                lateralSeed = i;
                break;
            }
        }
        Assert.True(lateralSeed >= 0);

        // With small reach limit (e.g. 5 mm on a cylinder of radius 10), it cannot select all 128 triangles
        int[] boundedRegion = selector.GrowRegion(
            lateralSeed,
            SelectionMode.SmoothSurface,
            toleranceDegrees: 30f,
            reachLimit: 5.0f);

        Assert.True(boundedRegion.Length < segments * 2, $"Expected bounded region < 128, got {boundedRegion.Length}");
        Assert.True(boundedRegion.Length >= 2);
    }

    [Fact]
    public void GrowRegion_OnObj1Assemblaggio_PegSeed_FlatFacePreventsSpill()
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../examples/obj_1_Assemblaggio.stl");
        if (!File.Exists(path))
        {
            path = Path.GetFullPath("examples/obj_1_Assemblaggio.stl");
        }

        if (!File.Exists(path))
        {
            return; // Skip if example STL not in relative path
        }

        using var fs = File.OpenRead(path);
        var (mesh, _) = StlReader.Read(fs);
        var selector = new RegionSelector(mesh);

        // Find a seed triangle on the top surface of the peg (Z near max, normal pointing +Z)
        float maxZ = mesh.BBox.Max.Z;
        int pegTopSeed = -1;
        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            Vector3 n = mesh.TriangleNormals.Span[i];
            if (n.Z > 0.99f)
            {
                mesh.GetTriangle(i, out Vector3 v0, out Vector3 v1, out Vector3 v2);
                float avgZ = (v0.Z + v1.Z + v2.Z) / 3f;
                if (avgZ >= maxZ - 1.0f)
                {
                    pegTopSeed = i;
                    break;
                }
            }
        }

        Assert.True(pegTopSeed >= 0, "Peg top seed triangle not found");

        // In FlatFace mode with 10 degree tolerance, it selects only coplanar top face and CANNOT drift to 1498 triangles!
        int[] flatSelection = selector.GrowRegion(pegTopSeed, SelectionMode.FlatFace, toleranceDegrees: 10f);
        Assert.True(flatSelection.Length < 100, $"FlatFace should be confined to top face (< 100 tris), got {flatSelection.Length}");

        // SmoothSurface at 30 deg without reach reproduces baseline flood of 1498 triangles
        int[] smoothFlood = selector.GrowRegion(pegTopSeed, SelectionMode.SmoothSurface, toleranceDegrees: 30f);
        Assert.Equal(1498, smoothFlood.Length);
    }

    [Fact]
    public void ModifySelection_SupportsReplaceAddAndSubtractKeepingSortedOrder()
    {
        int[] initial = [0, 1, 5, 8];
        int[] delta = [1, 2, 3, 8, 10];

        int[] added = RegionSelector.ModifySelection(initial, delta, SelectionModifyMode.Add);
        Assert.Equal([0, 1, 2, 3, 5, 8, 10], added);

        int[] subtracted = RegionSelector.ModifySelection(added, [1, 3, 10], SelectionModifyMode.Subtract);
        Assert.Equal([0, 2, 5, 8], subtracted);

        int[] replaced = RegionSelector.ModifySelection(subtracted, [4, 9], SelectionModifyMode.Replace);
        Assert.Equal([4, 9], replaced);
    }

    private static IndexedMesh LoadMesh(List<Tri> tris)
    {
        using MemoryStream ms = new();
        StlFixtures.WriteBinary(ms, tris);
        ms.Position = 0;
        return StlReader.Read(ms).Mesh;
    }
}

