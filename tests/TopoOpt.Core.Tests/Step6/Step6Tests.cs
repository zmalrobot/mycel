using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TopoOpt.Core.Fem;
using TopoOpt.Core.Geometry;
using TopoOpt.Core.Optimization;
using TopoOpt.Core.Persistence;
using TopoOpt.Core.PostProcessing;
using TopoOpt.Core.Voxelization;
using TopoOpt.Core.Workflow;
using Xunit;

namespace TopoOpt.Core.Tests.Step6;

public sealed class Step6Tests
{
    [Fact]
    public void T1_IsoSurface_Sphere_ClosedManifold_Euler2_And_VolumeWithin3Percent()
    {
        // Stated resolution: 32x32x32 grid, h = 1.0 mm, analytic sphere radius R = 12.0 mm centered at (16, 16, 16)
        int n = 32;
        float h = 1.0f;
        var grid = new GridSpec(Vector3.Zero, h, n, n, n);
        float[] density = new float[n * n * n];

        Vector3 center = new(16.0f, 16.0f, 16.0f);
        float radius = 12.0f;

        for (int iz = 0; iz < n; iz++)
        {
            for (int iy = 0; iy < n; iy++)
            {
                for (int ix = 0; ix < n; ix++)
                {
                    Vector3 pt = new(ix + 0.5f, iy + 0.5f, iz + 0.5f);
                    float dist = Vector3.Distance(pt, center);
                    // Smooth signed distance ramp across 2h so threshold 0.5 lies exactly at dist == radius
                    float val = Math.Clamp(0.5f + (radius - dist) / (2.0f * (float)h), 0.0f, 1.0f);
                    density[(int)grid.ElementIndex(ix, iy, iz)] = val;
                }
            }
        }

        IndexedMesh mesh = SurfaceNetsExtractor.Extract(grid, density, threshold: 0.5f);

        Assert.True(mesh.VertexCount > 0);
        Assert.True(mesh.TriangleCount > 0);

        var (openEdges, nonManifoldEdges, uniqueEdges, components) = AnalyzeMeshTopology(mesh);
        Assert.Equal(0, openEdges);
        Assert.Equal(0, nonManifoldEdges);
        Assert.Equal(1, components);

        // Euler characteristic: V - E + F = 2 for a closed genus-0 sphere
        int euler = mesh.VertexCount - uniqueEdges + mesh.TriangleCount;
        Assert.Equal(2, euler);

        // Analytic sphere volume = 4/3 * pi * R^3
        double analyticVolume = (4.0 / 3.0) * Math.PI * Math.Pow(radius, 3);
        double meshVolume = Math.Abs(mesh.SignedVolume);
        double relError = Math.Abs(meshVolume - analyticVolume) / analyticVolume;

        Assert.True(relError < 0.03, $"Expected sphere volume within 3% of {analyticVolume:F2}, got {meshVolume:F2} (relError={relError:P2})");
    }

    [Fact]
    public void T2_IsoSurface_TwoSpheres_And_CubeTouchingBorder()
    {
        // Part A: Two disjoint spheres -> 2 connected components, Euler = 2 + 2 = 4
        var grid2 = new GridSpec(Vector3.Zero, 1.0f, 40, 20, 20);
        float[] rhoTwo = new float[40 * 20 * 20];
        Vector3 c1 = new(10f, 10f, 10f);
        Vector3 c2 = new(30f, 10f, 10f);
        float r = 6.0f;

        for (int iz = 0; iz < 20; iz++)
        {
            for (int iy = 0; iy < 20; iy++)
            {
                for (int ix = 0; ix < 40; ix++)
                {
                    Vector3 pt = new(ix + 0.5f, iy + 0.5f, iz + 0.5f);
                    float d = MathF.Min(Vector3.Distance(pt, c1), Vector3.Distance(pt, c2));
                    rhoTwo[(int)grid2.ElementIndex(ix, iy, iz)] = Math.Clamp(0.5f + (r - d) / 2.0f, 0.0f, 1.0f);
                }
            }
        }

        IndexedMesh meshTwo = SurfaceNetsExtractor.Extract(grid2, rhoTwo, threshold: 0.5f);
        var (open2, nonMan2, edges2, comps2) = AnalyzeMeshTopology(meshTwo);
        Assert.Equal(0, open2);
        Assert.Equal(0, nonMan2);
        Assert.Equal(2, comps2);
        Assert.Equal(4, meshTwo.VertexCount - edges2 + meshTwo.TriangleCount);

        // Part B: Solid cube touching all 6 faces of the grid border -> closed thanks to 1-voxel zero padding
        var gridCube = new GridSpec(new Vector3(10f, 20f, 30f), 2.0f, 10, 10, 10);
        float[] rhoCube = new float[10 * 10 * 10];
        Array.Fill(rhoCube, 1.0f);

        IndexedMesh meshCube = SurfaceNetsExtractor.Extract(gridCube, rhoCube, threshold: 0.5f);
        var (openC, nonManC, edgesC, compsC) = AnalyzeMeshTopology(meshCube);
        Assert.Equal(0, openC);
        Assert.Equal(0, nonManC);
        Assert.Equal(1, compsC);
        Assert.Equal(2, meshCube.VertexCount - edgesC + meshCube.TriangleCount);

        // Check coordinates are in original mm space [10..30, 20..40, 30..50]
        Assert.True(MathF.Abs(meshCube.BBox.Min.X - 10f) < 1.5f);
        Assert.True(MathF.Abs(meshCube.BBox.Max.X - 30f) < 1.5f);
        Assert.True(MathF.Abs(meshCube.BBox.Min.Y - 20f) < 1.5f);
        Assert.True(MathF.Abs(meshCube.BBox.Max.Y - 40f) < 1.5f);
        Assert.True(MathF.Abs(meshCube.BBox.Min.Z - 30f) < 1.5f);
        Assert.True(MathF.Abs(meshCube.BBox.Max.Z - 50f) < 1.5f);
    }

    [Fact]
    public async Task T3_ExportStl_RoundTrip_Watertight_VolumeMatches_And_HeaderValid()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "TopoOpt_Step6_Export_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var grid = new GridSpec(Vector3.Zero, 1.0f, 24, 24, 24);
            float[] density = new float[24 * 24 * 24];
            Vector3 center = new(12f, 12f, 12f);
            for (int iz = 0; iz < 24; iz++)
                for (int iy = 0; iy < 24; iy++)
                    for (int ix = 0; ix < 24; ix++)
                    {
                        float d = Vector3.Distance(new Vector3(ix + 0.5f, iy + 0.5f, iz + 0.5f), center);
                        density[(int)grid.ElementIndex(ix, iy, iz)] = Math.Clamp(0.5f + (8.0f - d) / 2.0f, 0f, 1f);
                    }

            string stlPath = Path.Combine(tempDir, "exported.stl");
            var (exportedMesh, reReadMesh, report, reReadVolume) = await WorkflowService.ExportIterationStlAsync(
                stlPath, grid, iteration: 42, physicalDensity: density, threshold: 0.5f, smoothingIterations: 3);

            Assert.True(report.IsWatertight, $"Exported STL must be watertight (OpenEdges={report.OpenEdges}, NonManifold={report.NonManifoldEdges})");
            Assert.Equal(0, report.OpenEdges);
            Assert.Equal(0, report.NonManifoldEdges);

            double expectedVol = Math.Abs(exportedMesh.SignedVolume);
            Assert.True(Math.Abs(reReadVolume - expectedVol) / expectedVol < 1e-5,
                $"Re-imported volume {reReadVolume:F4} must match exported mesh volume {expectedVol:F4}");

            // Check 80-byte binary header starts with "TopoOpt it=42 th=0.5"
            byte[] headerBytes = new byte[80];
            using (var fs = File.OpenRead(stlPath))
            {
                fs.ReadExactly(headerBytes);
            }
            string headerText = Encoding.ASCII.GetString(headerBytes).TrimEnd('\0');
            Assert.StartsWith("TopoOpt it=42 th=0.5", headerText);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task T4_RunStore_100Iterations_QuantisationBound_CrashRecovery_InterruptedReopen_And_KeepEveryN()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "TopoOpt_Step6_Store_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var grid = new GridSpec(Vector3.Zero, 1.0f, 6, 5, 4);
            int totalElements = grid.Nx * grid.Ny * grid.Nz;
            byte[] domain = new byte[totalElements];
            byte[] passive = new byte[totalElements];
            Array.Fill(domain, (byte)1);

            // 1. Write 100 iterations & verify quantisation error bound <= 1/510
            string run100Folder = Path.Combine(tempDir, "run_100");
            var meta100 = new RunMetadata
            {
                RunName = "beam100",
                Grid = GridSpecDto.FromGrid(grid),
                Options = new OptimizerOptionsDto(0.3, 1.2, 3.0, 0.2, 100, 1e-6, false, false, true, KeepEveryN: 1)
            };

            var rng = new Random(12345);
            var originalDensities = new float[100][];

            using (var writer = new RunWriter(run100Folder, meta100, domain, passive))
            {
                for (int it = 1; it <= 100; it++)
                {
                    float[] phys = new float[totalElements];
                    for (int e = 0; e < totalElements; e++)
                    {
                        phys[e] = (float)rng.NextDouble();
                    }
                    originalDensities[it - 1] = phys;

                    var snap = new IterationSnapshot
                    {
                        Iteration = it,
                        Compliance = 1000.0 / it,
                        VolumeFraction = 0.3,
                        MaxChange = 0.1 / it,
                        GreyLevel = 20.0,
                        SolverIterations = 50,
                        SolverResidual = 1e-6,
                        Timings = new IterationTimings(10, 1, 1, 1, 13),
                        PhysicalDensity = phys,
                        DesignDensity = phys
                    };
                    await writer.WriteSnapshotAsync(snap, isFinal: it == 100);
                }
                writer.FinalizeRun(RunStatus.Completed);
            }

            var reader100 = RunReader.Open(run100Folder);
            Assert.Equal(RunStatus.Completed, reader100.Metadata.Status);
            int[] avail100 = reader100.GetAvailableIterations();
            Assert.Equal(100, avail100.Length);

            double maxQuantErrorBound = (1.0 / 510.0) + 1e-6;
            for (int it = 1; it <= 100; it++)
            {
                var (hdr, readRho) = reader100.ReadIteration(it);
                Assert.Equal(it, hdr.Iteration);
                float[] expected = originalDensities[it - 1];
                for (int e = 0; e < totalElements; e++)
                {
                    double err = Math.Abs(readRho[e] - expected[e]);
                    Assert.True(err <= maxQuantErrorBound,
                        $"Iteration {it} element {e}: quantisation error {err} exceeded 1/510 ({1.0 / 510.0})");
                }
            }

            // 2. Simulated crash between temp write and rename + interrupted run re-open
            string crashFolder = Path.Combine(tempDir, "run_crash");
            var metaCrash = new RunMetadata
            {
                RunName = "crash_test",
                Grid = GridSpecDto.FromGrid(grid),
                Options = new OptimizerOptionsDto(0.3, 1.2, 3.0, 0.2, 10, 1e-6, false, false, true, KeepEveryN: 1)
            };

            using (var crashWriter = new RunWriter(crashFolder, metaCrash, domain, passive))
            {
                crashWriter.SimulateCrashOnIteration = 4;
                for (int it = 1; it <= 3; it++)
                {
                    await crashWriter.WriteSnapshotAsync(new IterationSnapshot
                    {
                        Iteration = it,
                        Compliance = 500.0,
                        VolumeFraction = 0.3,
                        PhysicalDensity = originalDensities[it - 1],
                        DesignDensity = originalDensities[it - 1]
                    });
                }

                await Assert.ThrowsAsync<IOException>(async () =>
                {
                    await crashWriter.WriteSnapshotAsync(new IterationSnapshot
                    {
                        Iteration = 4,
                        Compliance = 400.0,
                        VolumeFraction = 0.3,
                        PhysicalDensity = originalDensities[3],
                        DesignDensity = originalDensities[3]
                    });
                });
                // Notice we do NOT call FinalizeRun: status on disk remains Running and a .tmp file exists!
            }

            var crashReader = RunReader.Open(crashFolder, isLiveActiveRun: false);
            Assert.Equal(RunStatus.Interrupted, crashReader.Metadata.Status);
            int[] crashAvail = crashReader.GetAvailableIterations();
            Assert.Equal(new[] { 1, 2, 3 }, crashAvail); // Half-written iteration 4 (.tmp) is invisible!

            // 3. KeepEveryN = 5 writes iterations 1, 5, 10, 15, 20
            string keepNFolder = Path.Combine(tempDir, "run_keep5");
            var metaKeep5 = new RunMetadata
            {
                RunName = "keep5",
                Grid = GridSpecDto.FromGrid(grid),
                Options = new OptimizerOptionsDto(0.3, 1.2, 3.0, 0.2, 20, 1e-6, false, false, true, KeepEveryN: 5)
            };

            using (var keepWriter = new RunWriter(keepNFolder, metaKeep5, domain, passive))
            {
                for (int it = 1; it <= 20; it++)
                {
                    await keepWriter.WriteSnapshotAsync(new IterationSnapshot
                    {
                        Iteration = it,
                        Compliance = 100.0,
                        VolumeFraction = 0.3,
                        PhysicalDensity = originalDensities[it - 1],
                        DesignDensity = originalDensities[it - 1]
                    }, isFinal: it == 20);
                }
                keepWriter.FinalizeRun(RunStatus.Completed);
            }

            var keepReader = RunReader.Open(keepNFolder);
            Assert.Equal(new[] { 1, 5, 10, 15, 20 }, keepReader.GetAvailableIterations());
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task T5_Backpressure_SlowWriter_MakesOptimiserWait_ZeroSnapshotsLost()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "TopoOpt_Step6_Backpressure_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var grid = new GridSpec(Vector3.Zero, 1.0f, 4, 4, 4);
            int totalElements = grid.Nx * grid.Ny * grid.Nz;
            byte[] domain = new byte[totalElements];
            Array.Fill(domain, (byte)1);

            var meta = new RunMetadata
            {
                RunName = "backpressure",
                Grid = GridSpecDto.FromGrid(grid),
                Options = new OptimizerOptionsDto(0.3, 1.2, 3.0, 0.2, 25, 1e-6, false, false, true, KeepEveryN: 1)
            };

            using var writer = new RunWriter(tempDir, meta, domain, []);
            writer.SimulatedWriteDelay = TimeSpan.FromMilliseconds(20); // Deliberately slow writer

            using var coordinator = new OptimizationCoordinator();
            var sw = Stopwatch.StartNew();

            await coordinator.StartCustomProducerAsync(writer, (emit, ctrl, ct) =>
            {
                for (int it = 1; it <= 25; it++)
                {
                    float[] phys = new float[totalElements];
                    Array.Fill(phys, (it % 10) * 0.1f);
                    emit(new IterationSnapshot
                    {
                        Iteration = it,
                        Compliance = 100.0 / it,
                        VolumeFraction = 0.3,
                        PhysicalDensity = phys,
                        DesignDensity = phys
                    });
                }
                return new TopologyOptimizationResult { Converged = true, TotalIterations = 25 };
            });

            sw.Stop();

            Assert.Equal(25, coordinator.EmittedSnapshotCount);
            Assert.Equal(25, writer.WrittenSnapshotCount);
            Assert.Equal(coordinator.EmittedRollingChecksum, writer.RollingStreamChecksum);

            var reader = RunReader.Open(tempDir);
            Assert.Equal(25, reader.GetAvailableIterations().Length);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task T6_LatestWinsDisplaySlot_UnderFlood_NeverQueuesMoreThanOne()
    {
        var slot = new LatestWinsDisplaySlot<IterationSnapshot>();
        int lastDisplayedIter = 0;

        // Start one slow extraction on the slot while flooding 500 snapshots
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        slot.Publish(new IterationSnapshot { Iteration = 1 });

        Task firstExtract = slot.PollAndExtractOnThreadPoolAsync(async (snap, ct) =>
        {
            await barrier.Task;
            Volatile.Write(ref lastDisplayedIter, snap.Iteration);
        });

        for (int it = 2; it <= 500; it++)
        {
            slot.Publish(new IterationSnapshot { Iteration = it });
            Assert.True(slot.PendingCount <= 1, $"PendingCount exceeded 1: {slot.PendingCount}");
            // Polling while firstExtract is still in flight returns false immediately
            bool started = await slot.PollAndExtractOnThreadPoolAsync((_, _) => Task.CompletedTask);
            Assert.False(started);
        }

        Assert.Equal(1, slot.MaxObservedPendingCount);

        // Release first extraction and poll once more to consume the latest snapshot (500)
        barrier.SetResult();
        await firstExtract;

        bool secondStarted = await slot.PollAndExtractOnThreadPoolAsync((snap, _) =>
        {
            Volatile.Write(ref lastDisplayedIter, snap.Iteration);
            return Task.CompletedTask;
        });

        Assert.True(secondStarted);
        Assert.Equal(500, lastDisplayedIter);
        Assert.Equal(0, slot.PendingCount);
        Assert.Equal(1, slot.MaxObservedPendingCount);
    }

    [Fact]
    public async Task T7_Resume_FromLastCompleteIteration_MatchesUninterruptedRun()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "TopoOpt_Step6_Resume_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // Small cantilever beam: 8 x 4 x 4 elements
            var grid = new GridSpec(Vector3.Zero, 1.0f, 8, 4, 4);
            int total = grid.Nx * grid.Ny * grid.Nz;
            byte[] domain = new byte[total];
            Array.Fill(domain, (byte)1);

            var fixedDofs = new List<int>();
            for (int k = 0; k <= grid.Nz; k++)
                for (int j = 0; j <= grid.Ny; j++)
                {
                    int n = (int)grid.NodeIndex(0, j, k);
                    fixedDofs.Add(3 * n + 0);
                    fixedDofs.Add(3 * n + 1);
                    fixedDofs.Add(3 * n + 2);
                }

            int tipNode = (int)grid.NodeIndex(grid.Nx, grid.Ny / 2, grid.Nz / 2);
            var tipPatch = new SurfacePatch([], [tipNode], [1.0], (Vector3.Zero, Vector3.Zero), true);
            var loads = new List<LoadCase> { new(tipPatch, new Vector3(0, 0, -100f)) };
            var mat = new FeMaterial(3000.0, 0.35, 1e-6);

            // 1. Uninterrupted 6-iteration run
            var modelFull = new FeModel(grid, domain, mat, fixedDofs.ToArray(), loads);
            var optsFull = new OptimizerOptions
            {
                VolumeFraction = 0.4,
                FilterRadiusMm = 1.6,
                MaxIterations = 6,
                SolverToleranceInitial = 1e-6,
                SolverToleranceFinal = 1e-6
            };
            var resFull = TopologyOptimizer.Run(modelFull, optsFull);

            // 2. Run 3 iterations into RunWriter, then re-open and continue from iteration 4 to 6
            string runFolder = Path.Combine(tempDir, "interrupted_run");
            var meta = new RunMetadata
            {
                RunName = "cantilever",
                Grid = GridSpecDto.FromGrid(grid),
                Options = new OptimizerOptionsDto(0.4, 1.6, 3.0, 0.2, 6, 1e-6, false, false, true, 1)
            };

            var modelPart1 = new FeModel(grid, domain, mat, fixedDofs.ToArray(), loads);
            using (var writer1 = new RunWriter(runFolder, meta, domain, []))
            {
                using var coord1 = new OptimizationCoordinator();
                var opts1 = new OptimizerOptions
                {
                    VolumeFraction = 0.4,
                    FilterRadiusMm = 1.6,
                    MaxIterations = 3,
                    SolverToleranceInitial = 1e-6,
                    SolverToleranceFinal = 1e-6
                };
                await coord1.StartRunAsync(modelPart1, opts1, writer1);
            }

            // Re-open run and read checkpoint
            var reader = RunReader.Open(runFolder);
            var ckpt = reader.ReadLatestCheckpoint();
            Assert.NotNull(ckpt);
            Assert.Equal(3, ckpt.Value.Iteration);

            var modelPart2 = new FeModel(grid, domain, mat, fixedDofs.ToArray(), loads);
            using (var writer2 = new RunWriter(runFolder, reader.Metadata, domain, [], isResume: true))
            {
                using var coord2 = new OptimizationCoordinator();
                var opts2 = new OptimizerOptions
                {
                    VolumeFraction = 0.4,
                    FilterRadiusMm = 1.6,
                    StartIteration = ckpt.Value.Iteration + 1,
                    MaxIterations = 6,
                    InitialDensity = ckpt.Value.DesignDensity,
                    SolverToleranceInitial = 1e-6,
                    SolverToleranceFinal = 1e-6
                };
                var resResumed = await coord2.StartRunAsync(modelPart2, opts2, writer2);

                // Compare final compliance and physical densities
                double relCompDiff = Math.Abs(resResumed.FinalCompliance - resFull.FinalCompliance) / resFull.FinalCompliance;
                Assert.True(relCompDiff < 1e-5, $"Expected resumed compliance {resResumed.FinalCompliance} to match full run {resFull.FinalCompliance} (relDiff={relCompDiff})");

                for (int e = 0; e < total; e++)
                {
                    Assert.True(Math.Abs(resResumed.FinalPhysicalDensity[e] - resFull.FinalPhysicalDensity[e]) < 1e-4f);
                }
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task T8_ThreadingSmokeTest_1000IterationsAt1kHz_And_ScrubbingSequenceGuard()
    {
        // Part A: 1000 iterations at 1 kHz with fake optimiser + fake viewport + random Stop
        using var coordinator = new OptimizationCoordinator();
        var unobservedExceptions = new List<Exception>();
        int viewportUpdates = 0;

        using var pollCts = new CancellationTokenSource();
        Task pollLoop = Task.Run(async () =>
        {
            try
            {
                while (!pollCts.Token.IsCancellationRequested)
                {
                    await coordinator.DisplaySlot.PollAndExtractOnThreadPoolAsync(async (snap, ct) =>
                    {
                        await Task.Yield();
                        Interlocked.Increment(ref viewportUpdates);
                    }, pollCts.Token);
                    await Task.Delay(5, pollCts.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                lock (unobservedExceptions) unobservedExceptions.Add(ex);
            }
        });

        int stopAfterMs = new Random(42).Next(60, 180);
        _ = Task.Run(async () =>
        {
            await Task.Delay(stopAfterMs);
            await coordinator.StopAndFinalizeAsync();
        });

        var result = await coordinator.StartCustomProducerAsync(null, (emit, ctrl, ct) =>
        {
            float[] dummy = new float[8];
            int count = 0;
            for (int it = 1; it <= 1000; it++)
            {
                if (ctrl.CheckAndBlock(ct) || ct.IsCancellationRequested)
                    break;

                emit(new IterationSnapshot
                {
                    Iteration = it,
                    Compliance = 1000.0 - it,
                    VolumeFraction = 0.3,
                    PhysicalDensity = dummy,
                    DesignDensity = dummy
                });
                count = it;
                Thread.SpinWait(500); // ~1 kHz pace
            }
            return new TopologyOptimizationResult { TotalIterations = count };
        });

        pollCts.Cancel();
        await pollLoop;

        Assert.Empty(unobservedExceptions);
        Assert.True(coordinator.EmittedSnapshotCount > 0 && coordinator.EmittedSnapshotCount <= 1000);

        // Part B: Playback scrubbing rapid random seeks never display a stale iteration
        using var scrubber = new PlaybackScrubber();
        int finalDisplayedIter = -1;
        var seekTasks = new List<Task>();

        // Issue 20 rapid seeks where earlier seeks take LONGER than later seeks
        for (int s = 1; s <= 20; s++)
        {
            int targetIter = s;
            int delayMs = (21 - s) * 3; // Seek 1 takes 60ms, Seek 20 takes 3ms!
            seekTasks.Add(scrubber.SeekIterationAsync(
                targetIter,
                async (iter, ct) =>
                {
                    await Task.Delay(delayMs, ct);
                    return new IndexedMesh([], []);
                },
                (iter, _) =>
                {
                    Volatile.Write(ref finalDisplayedIter, iter);
                    return Task.CompletedTask;
                }));
        }

        await Task.WhenAll(seekTasks);
        Assert.Equal(20, scrubber.DisplayedIteration);
        Assert.Equal(20, finalDisplayedIter);
    }

    private static (int OpenEdges, int NonManifoldEdges, int UniqueEdges, int ConnectedComponents) AnalyzeMeshTopology(IndexedMesh mesh)
    {
        ReadOnlySpan<int> indices = mesh.Indices.Span;
        int triCount = mesh.TriangleCount;

        // Directed half-edge map: (u, v) -> count, and undirected edge map: (min, max) -> count
        var directedEdges = new Dictionary<(int, int), int>(triCount * 3);
        var undirectedEdges = new Dictionary<(int, int), List<int>>(triCount * 3);

        for (int t = 0; t < triCount; t++)
        {
            int i0 = indices[t * 3];
            int i1 = indices[t * 3 + 1];
            int i2 = indices[t * 3 + 2];

            AddEdge(i0, i1, t);
            AddEdge(i1, i2, t);
            AddEdge(i2, i0, t);
        }

        void AddEdge(int a, int b, int tri)
        {
            directedEdges[(a, b)] = directedEdges.GetValueOrDefault((a, b)) + 1;
            var key = a < b ? (a, b) : (b, a);
            if (!undirectedEdges.TryGetValue(key, out var list))
            {
                list = new List<int>(2);
                undirectedEdges[key] = list;
            }
            list.Add(tri);
        }

        int openEdges = 0;
        int nonManifoldEdges = 0;

        foreach (var kvp in undirectedEdges)
        {
            var (u, v) = kvp.Key;
            int totalIncidence = kvp.Value.Count;
            if (totalIncidence == 1)
            {
                openEdges++;
            }
            else if (totalIncidence != 2)
            {
                nonManifoldEdges++;
            }
            else
            {
                // For a consistent 2-manifold orientation, (u,v) must appear once and (v,u) once
                int fwd = directedEdges.GetValueOrDefault((u, v));
                int rev = directedEdges.GetValueOrDefault((v, u));
                if (fwd != 1 || rev != 1)
                {
                    nonManifoldEdges++;
                }
            }
        }

        // Count connected triangle components via BFS
        bool[] visited = new bool[triCount];
        int components = 0;
        var queue = new Queue<int>();

        // Build triangle adjacency from undirectedEdges
        var triAdj = new List<int>[triCount];
        for (int t = 0; t < triCount; t++) triAdj[t] = new List<int>(3);
        foreach (var tris in undirectedEdges.Values)
        {
            for (int i = 0; i < tris.Count; i++)
                for (int j = i + 1; j < tris.Count; j++)
                {
                    triAdj[tris[i]].Add(tris[j]);
                    triAdj[tris[j]].Add(tris[i]);
                }
        }

        for (int t = 0; t < triCount; t++)
        {
            if (visited[t]) continue;
            components++;
            visited[t] = true;
            queue.Enqueue(t);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                foreach (int nb in triAdj[cur])
                {
                    if (!visited[nb])
                    {
                        visited[nb] = true;
                        queue.Enqueue(nb);
                    }
                }
            }
        }

        return (openEdges, nonManifoldEdges, undirectedEdges.Count, components);
    }
}
