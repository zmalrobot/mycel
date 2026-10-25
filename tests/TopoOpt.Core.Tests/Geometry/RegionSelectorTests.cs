using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using TopoOpt.Core.Geometry;
using TopoOpt.Core.Tests.Fixtures;
using Xunit;

namespace TopoOpt.Core.Tests.Geometry;

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

