using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using TopoOpt.Core.Geometry;
using TopoOpt.Core.Tests.Fixtures;
using TopoOpt.Core.Voxelization;
using Xunit;
using Xunit.Abstractions;

namespace TopoOpt.Core.Tests.Voxelization;

public class VoxelBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public VoxelBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Skip = "Explicit benchmark for 1M triangles to 2M voxels")]
    public void Benchmark_1MTriangles_To_2MVoxels()
    {
        // 1,001,112 triangles
        _output.WriteLine("Generating 1M triangle sphere fixture...");
        var tris = StlFixtures.Sphere(radius: 50.0f, latSegments: 708, lonSegments: 708);
        using var ms = new MemoryStream();
        StlFixtures.WriteBinary(ms, tris);
        ms.Position = 0;

        var (mesh, report) = StlReader.Read(ms);
        _output.WriteLine($"Loaded mesh with {mesh.TriangleCount} triangles and {mesh.VertexCount} vertices.");

        // Target: ~2M solid elements
        var grid = GridSpec.FromTargetElementCount(mesh, 2_000_000);
        _output.WriteLine($"Grid: {grid.Nx}x{grid.Ny}x{grid.Nz} = {grid.TotalElements} total bounding box elements, h = {grid.H:F3} mm");

        var sw = Stopwatch.StartNew();
        long memBefore = GC.GetTotalMemory(true);

        var (model, _) = Voxeliser.Voxelise(mesh, grid);

        sw.Stop();
        long memAfter = GC.GetTotalMemory(false);

        _output.WriteLine($"Voxelisation completed in {sw.ElapsedMilliseconds} ms ({sw.Elapsed.TotalSeconds:F2} s).");
        _output.WriteLine($"Solid count: {model.SolidCount} elements. Memory delta: {(memAfter - memBefore) / (1024 * 1024)} MB.");

        Assert.True(model.SolidCount > 0);
        Assert.True(sw.Elapsed.TotalSeconds < 30.0, "Voxelisation of 1M triangles should complete within reasonable time");
    }
}
