using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Mycel.Core.Geometry;
using Mycel.Core.Tests.Fixtures;
using Mycel.Core.Workflow;
using Xunit;

namespace Mycel.Core.Tests.Geometry;

public sealed class RegionMetricsTests
{
    private static IndexedMesh LoadMesh(List<Tri> tris)
    {
        using MemoryStream ms = new();
        StlFixtures.WriteBinary(ms, tris);
        ms.Position = 0;
        return StlReader.Read(ms).Mesh;
    }

    [Fact]
    public void LoadRegion_BoxFace_ComputesAnalyticSurfaceAreaAndPressure()
    {
        // 20 x 20 x 20 mm Box -> each face area = 400 mm^2
        IndexedMesh box = LoadMesh(StlFixtures.Box(new Vector3(20f, 20f, 20f)));
        RegionSelector selector = new(box);

        int[] faceTris = selector.GrowRegion(0, SelectionMode.FlatFace, toleranceDegrees: 10f);
        Assert.Equal(2, faceTris.Length);

        var load = new LoadRegion
        {
            Name = "Top Load",
            Triangles = faceTris,
            MagnitudeNewtons = 800.0f
        };

        load.UpdateGeometryMetrics(box);

        // Area must be 400 mm^2 within numerical tolerance
        Assert.InRange(load.SurfaceAreaMm2, 399.9f, 400.1f);

        // Nominal pressure = 800 N / 400 mm^2 = 2.0 MPa
        Assert.InRange(load.NominalPressureMpa, 1.999f, 2.001f);

        // Coverage = 400 / (6 * 400) = 16.67%
        Assert.InRange(load.ModelSurfaceCoveragePercent, 16.5f, 16.8f);
        Assert.False(load.IsHighCoverage);
        Assert.False(load.IsWeakNormal);
        Assert.Equal(1, load.ConnectedComponentCount);
        Assert.False(load.IsDisconnected);
    }

    [Fact]
    public void LoadRegion_CylinderLateralSurface_DetectsWeakNormalAndSurfaceAnchor()
    {
        int segments = 48;
        float radius = 10f;
        float height = 30f;
        IndexedMesh cyl = LoadMesh(StlFixtures.Cylinder(radius, height, segments, holeRadius: 0f));
        RegionSelector selector = new(cyl);

        // Find lateral seed
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

        int[] lateralTris = selector.GrowRegion(lateralSeed, SelectionMode.SmoothSurface, toleranceDegrees: 30f);
        Assert.Equal(segments * 2, lateralTris.Length);

        var load = new LoadRegion
        {
            Name = "Curved Lateral Load",
            Triangles = lateralTris,
            MagnitudeNewtons = 500f,
            DirectionMode = LoadDirectionMode.SurfaceNormalInward,
            SeedTriangle = lateralSeed
        };

        load.UpdateGeometryMetrics(cyl, lateralSeed);

        // Centroid of the full cylinder lateral wall is at the cylinder axis: (0, 0, Z_mid)
        Assert.InRange(load.Centroid.X, -0.01f, 0.01f);
        Assert.InRange(load.Centroid.Y, -0.01f, 0.01f);

        // But AnchorPoint MUST lie on the physical surface of radius 10!
        float anchorRadius = MathF.Sqrt(load.AnchorPoint.X * load.AnchorPoint.X + load.AnchorPoint.Y * load.AnchorPoint.Y);
        Assert.InRange(anchorRadius, radius - 0.05f, radius + 0.05f);

        // Normal consistency ratio across the entire circumference cancels out opposing normals
        Assert.True(load.NormalConsistencyRatio < 0.10f, $"Expected weak normal ratio < 0.10, got {load.NormalConsistencyRatio}");
        Assert.True(load.IsWeakNormal);

        // When weak, ComputeUnitDirection must fall back to the seed normal
        Vector3 unitDir = load.ComputeUnitDirection();
        Vector3 seedNormal = cyl.TriangleNormals.Span[lateralSeed];
        // SurfaceNormalInward -> points opposite to seed normal
        float dotWithSeed = Vector3.Dot(unitDir, -Vector3.Normalize(seedNormal));
        Assert.True(dotWithSeed > 0.999f, $"Direction should match inward seed normal, dot={dotWithSeed}");
    }

    [Fact]
    public void LoadRegion_SampleDistributedPoints_GeneratesCorrectBoundedCount()
    {
        IndexedMesh cyl = LoadMesh(StlFixtures.Cylinder(10f, 30f, 64, holeRadius: 0f));
        RegionSelector selector = new(cyl);

        int[] allTris = selector.GrowRegion(0, SelectionMode.SmoothSurface, toleranceDegrees: 30f);
        var load = new LoadRegion
        {
            Triangles = allTris
        };
        load.UpdateGeometryMetrics(cyl);

        var samples = load.SampleDistributedPoints(cyl, maxSamples: 40);
        Assert.Equal(40, samples.Count);

        foreach (var (pt, normal) in samples)
        {
            Assert.True(normal.LengthSquared() > 0.9f);
        }
    }
}

