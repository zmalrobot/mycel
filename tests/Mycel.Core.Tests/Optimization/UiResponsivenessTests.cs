using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Mycel.Core.Fem;
using Mycel.Core.Optimization;
using Mycel.Core.Voxelization;
using Xunit;
using Xunit.Abstractions;

namespace Mycel.Core.Tests.Optimization;

public sealed class UiResponsivenessTests
{
    private readonly ITestOutputHelper _output;

    public UiResponsivenessTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Heartbeat_DuringOptimizationRun_NeverGapsAbove100Ms()
    {
        // 1. Setup a representative 3D optimization domain: 30x10x10 = 3,000 active elements
        int nx = 30, ny = 10, nz = 10;
        double h = 1.0;
        var grid = new GridSpec(Vector3.Zero, (float)h, nx, ny, nz);
        int totalElements = nx * ny * nz;
        byte[] domain = new byte[totalElements];
        Array.Fill(domain, (byte)1);

        var mat = new FeMaterial(3000.0, 0.3, 1e-6);

        // Fix all nodes at x = 0
        var fixedNodes = new List<int>();
        for (int k = 0; k <= nz; k++)
            for (int j = 0; j <= ny; j++)
                fixedNodes.Add((int)grid.NodeIndex(0, j, k));

        int[] fixedDofs = FeModel.FixDofs(fixedNodes, true, true, true);

        // Load at x = nx, y = ny/2, z = 0
        int loadNode = (int)grid.NodeIndex(nx, ny / 2, 0);
        var patch = new SurfacePatch(
            Array.Empty<VoxelFace>(),
            new[] { loadNode },
            new[] { h * h },
            (new Vector3((float)(nx * h), (float)(ny * h * 0.5f), 0), new Vector3((float)(nx * h), (float)(ny * h * 0.5f), 0)),
            true);

        var loads = new[] { new LoadCase(patch, new Vector3(0, 0, -100.0f)) };
        var feModel = new FeModel(grid, domain, mat, fixedDofs, loads);

        var options = new OptimizerOptions(
            VolumeFraction: 0.5,
            FilterRadiusMm: 2.0,
            Penal: 3.0,
            MoveLimit: 0.2,
            MaxIterations: 8,
            EminRatio: 1e-6,
            ProtectBoundaryHinges: false);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // 2. Start heartbeat thread (simulating UI thread tick at 10 ms interval)
        double maxGapMs = 0.0;
        int totalTicks = 0;
        using var heartbeatCts = new CancellationTokenSource();

        var heartbeatTask = Task.Run(async () =>
        {
            Stopwatch sw = Stopwatch.StartNew();
            long lastMs = sw.ElapsedMilliseconds;

            while (!heartbeatCts.Token.IsCancellationRequested)
            {
                await Task.Delay(10, heartbeatCts.Token).ConfigureAwait(false);
                long nowMs = sw.ElapsedMilliseconds;
                long deltaMs = nowMs - lastMs;
                lastMs = nowMs;

                if (deltaMs > maxGapMs)
                {
                    maxGapMs = deltaMs;
                }
                Interlocked.Increment(ref totalTicks);
            }
        }, heartbeatCts.Token);

        // 3. Run topology optimization asynchronously
        var result = await Task.Run(() => TopologyOptimizer.Run(feModel, options, ct: cts.Token));

        // Stop heartbeat
        heartbeatCts.Cancel();
        try { await heartbeatTask; } catch (OperationCanceledException) { }

        _output.WriteLine($"Completed {result.TotalIterations} iterations.");
        _output.WriteLine($"Total UI heartbeat ticks: {totalTicks}. Max observed heartbeat gap: {maxGapMs:F2} ms.");

        Assert.True(result.Snapshots.Count > 0);
        Assert.True(totalTicks > 5, "Heartbeat must have recorded multiple samples.");
        Assert.True(maxGapMs <= 100.0, $"UI-thread heartbeat gap {maxGapMs:F2} ms exceeded threshold of 100 ms!");
    }
}
