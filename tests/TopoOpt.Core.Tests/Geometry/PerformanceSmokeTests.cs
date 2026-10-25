using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using TopoOpt.Core.Geometry;
using TopoOpt.Core.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace TopoOpt.Core.Tests.Geometry;

public sealed class PerformanceSmokeTests
{
    private readonly ITestOutputHelper _output;

    public PerformanceSmokeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void OneMillionTriangleSphere_LoadBvhAndRaycast_MeetsPerformanceBudget()
    {
        // Only run during explicit performance benchmark invocations or when RUN_PERF_TESTS=1
        if (Environment.GetEnvironmentVariable("RUN_PERF_TESTS") != "1")
        {
            return;
        }

        // 708 x 708 sphere has 2 * 708 * (708 - 1) = 1,001,112 triangles
        List<Tri> sphere = StlFixtures.Sphere(50f, 708, 708);
        Assert.True(sphere.Count >= 1_000_000);

        using MemoryStream ms = new(84 + 50 * sphere.Count);
        StlFixtures.WriteBinary(ms, sphere);
        ms.Position = 0;

        Stopwatch sw = Stopwatch.StartNew();
        var (mesh, report) = StlReader.Read(ms);
        TimeSpan loadTime = sw.Elapsed;

        sw.Restart();
        Bvh bvh = new(mesh);
        TimeSpan bvhTime = sw.Elapsed;

        int rayCount = 10_000;
        uint state = 0x9E3779B9u;
        sw.Restart();
        int hits = 0;
        for (int i = 0; i < rayCount; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            float ox = ((state & 0xFFFFu) / 65535f - 0.5f) * 200f;
            float oy = (((state >> 16) & 0xFFFFu) / 65535f - 0.5f) * 200f;
            Vector3 origin = new(ox, oy, 150f);
            Vector3 dir = Vector3.Normalize(new Vector3(-ox * 0.5f, -oy * 0.5f, -150f));
            if (bvh.RayCast(origin, dir).IsHit)
            {
                hits++;
            }
        }
        TimeSpan pickTime = sw.Elapsed;
        double picksPerSec = rayCount / pickTime.TotalSeconds;

        _output.WriteLine($"Triangles: {mesh.TriangleCount:N0}, Vertices: {mesh.VertexCount:N0}");
        _output.WriteLine($"STL Read + Weld: {loadTime.TotalMilliseconds:F1} ms");
        _output.WriteLine($"BVH Build: {bvhTime.TotalMilliseconds:F1} ms");
        _output.WriteLine($"Raycast: {picksPerSec:N0} picks/s ({hits} hits)");

        Assert.True(report.IsWatertight);
        Assert.True(loadTime.TotalSeconds < 5.0);
        Assert.True(bvhTime.TotalSeconds < 5.0);
        Assert.True(picksPerSec > 50_000);
    }
}

