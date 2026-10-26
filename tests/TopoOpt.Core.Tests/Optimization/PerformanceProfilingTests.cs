using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using TopoOpt.Core.Fem;
using TopoOpt.Core.Optimization;
using TopoOpt.Core.Voxelization;
using Xunit;
using Xunit.Abstractions;

namespace TopoOpt.Core.Tests.Optimization;

public sealed class PerformanceProfilingTests
{
    private readonly ITestOutputHelper _output;

    public PerformanceProfilingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Profile_100kElements_MeasuresBreakdownAndAllocations()
    {
        // 100 x 40 x 25 = 100,000 voxels, 101 x 41 x 26 = 107,666 nodes
        int nx = 100, ny = 40, nz = 25;
        double h = 1.0;
        var grid = new GridSpec(Vector3.Zero, (float)h, nx, ny, nz);
        int totalElements = nx * ny * nz;
        byte[] domain = new byte[totalElements];
        Array.Fill(domain, (byte)1);

        var mat = new FeMaterial(3000.0, 0.3, 1e-6);

        // Fix x = 0 face
        var fixedNodes = new List<int>();
        for (int k = 0; k <= nz; k++)
            for (int j = 0; j <= ny; j++)
                fixedNodes.Add((int)grid.NodeIndex(0, j, k));

        int[] fixedDofs = FeModel.FixDofs(fixedNodes, true, true, true);

        // Single load at bottom corner
        int loadNode = (int)grid.NodeIndex(nx, 0, 0);
        var patch = new SurfacePatch(
            Array.Empty<VoxelFace>(),
            new[] { loadNode },
            new[] { h * h },
            (new Vector3((float)(nx * h), 0, 0), new Vector3((float)(nx * h), 0, 0)),
            true);

        var loads = new[] { new LoadCase(patch, new Vector3(0, 0, -100.0f)) };
        var feModel = new FeModel(grid, domain, mat, fixedDofs, loads);

        var options = new OptimizerOptions(
            VolumeFraction: 0.5,
            FilterRadiusMm: 2.0,
            Penal: 3.0,
            MoveLimit: 0.2,
            MaxIterations: 2,
            EminRatio: 1e-6,
            ProtectBoundaryHinges: false);

        // Warm up GC
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch sw = Stopwatch.StartNew();

        var result = TopologyOptimizer.Run(feModel, options);

        sw.Stop();
        long bytesAfter = GC.GetAllocatedBytesForCurrentThread();

        long totalAllocatedBytes = bytesAfter - bytesBefore;
        double allocMb = totalAllocatedBytes / (1024.0 * 1024.0);
        double msPerIteration = sw.Elapsed.TotalMilliseconds / result.Snapshots.Count;

        _output.WriteLine($"[100k Elements Profiling]");
        _output.WriteLine($"Elements: {totalElements:N0}, Nodes: {feModel.TotalNodes:N0}");
        _output.WriteLine($"Total iterations: {result.Snapshots.Count}, Total Time: {sw.Elapsed.TotalMilliseconds:F1} ms");
        _output.WriteLine($"Mean Time / Iteration: {msPerIteration:F1} ms");
        _output.WriteLine($"Total Thread Allocations: {allocMb:F2} MB ({allocMb / result.Snapshots.Count:F2} MB/iteration)");

        Assert.True(result.Snapshots.Count > 0);
    }
}

