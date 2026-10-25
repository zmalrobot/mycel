using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using TopoOpt.Core.Fem;
using TopoOpt.Core.Geometry;
using TopoOpt.Core.Optimization;
using TopoOpt.Core.Voxelization;

namespace TopoOpt.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        string cmd = args[0].ToLowerInvariant();
        try
        {
            if (cmd == "analyze" && args.Length >= 2)
            {
                return RunAnalyze(args[1]);
            }
            if (cmd == "optimize" && args.Length >= 2)
            {
                string? outDir = null;
                for (int i = 2; i < args.Length; i++)
                {
                    if ((args[i] == "--out" || args[i] == "-o") && i + 1 < args.Length)
                        outDir = args[++i];
                }
                return RunOptimize(args[1], outDir);
            }
            if (cmd == "--benchmark-fem")
            {
                return RunFemBenchmarks();
            }

            PrintUsage();
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("TopoOpt CLI — Usage:");
        Console.WriteLine("  topoopt analyze <model.json>");
        Console.WriteLine("  topoopt optimize <model.json> [--out <dir>]");
        Console.WriteLine("  topoopt --benchmark-fem");
    }

    private static int RunAnalyze(string jsonPath)
    {
        var (model, _) = LoadModel(jsonPath);
        Console.WriteLine($"Running static analysis on model ({model.ActiveElementCount:N0} active elements, {model.TotalDofs:N0} DOFs)...");

        var sw = Stopwatch.StartNew();
        var result = StaticAnalysis.Run(model, new StaticAnalysisOptions(Tolerance: 1e-6));
        sw.Stop();

        double maxVm = 0.0;
        foreach (double vm in result.VonMisesPerElement)
        {
            if (vm > maxVm) maxVm = vm;
        }

        Console.WriteLine($"Compliance:       {result.Compliance:F6}");
        Console.WriteLine($"Max Displacement: {result.MaxDisplacement:F6} mm");
        Console.WriteLine($"Max von Mises:    {maxVm:F6} MPa");
        Console.WriteLine($"Solve Time:       {sw.Elapsed.TotalMilliseconds:F1} ms ({result.SolverInfo.Iterations} iters, res: {result.SolverInfo.FinalResidual:E2})");
        return 0;
    }

    private static int RunOptimize(string jsonPath, string? outDir)
    {
        var (model, options) = LoadModel(jsonPath);
        outDir ??= Path.Combine("runs", Path.GetFileNameWithoutExtension(jsonPath));
        Directory.CreateDirectory(outDir);

        Console.WriteLine($"Optimizing model ({model.ActiveElementCount:N0} active elements, {model.TotalDofs:N0} DOFs, target vol: {options.VolumeFraction:P0})...");
        Console.WriteLine("Iter | Compliance | Volume  | dRho   | Grey % | PCG Iters (Residual) | Time (ms)");
        Console.WriteLine("-----+------------+---------+--------+--------+----------------------+----------");

        var progress = new Progress<IterationSnapshot>(s =>
        {
            Console.WriteLine($"{s.Iteration,4} | {s.Compliance,10:F3} | {s.VolumeFraction,7:F4} | {s.MaxChange,6:F4} | {s.GreyLevel,5:F1}% | {s.SolverIterations,4} it ({s.SolverResidual,7:E1}) | {s.TotalMs,8:F1}");
        });

        var result = TopologyOptimizer.Run(model, options, progress);

        // Save metrics CSV
        string csvPath = Path.Combine(outDir, "metrics.csv");
        using (var sw = new StreamWriter(csvPath, false, Encoding.UTF8))
        {
            sw.WriteLine("Iteration,Compliance,VolumeFraction,MaxChange,GreyLevel,SolverIterations,SolverResidual,SolveMs,SensitivityMs,FilterMs,UpdateMs,TotalMs");
            foreach (var s in result.Snapshots)
            {
                sw.WriteLine($"{s.Iteration},{s.Compliance:G8},{s.VolumeFraction:G8},{s.MaxChange:G8},{s.GreyLevel:G6},{s.SolverIterations},{s.SolverResidual:G6},{s.SolveMs:F2},{s.SensitivityMs:F2},{s.FilterMs:F2},{s.UpdateMs:F2},{s.TotalMs:F2}");
            }
        }

        // Save final physical density as raw binary float[] (.bin)
        string binPath = Path.Combine(outDir, "density.bin");
        using (var bw = new BinaryWriter(File.OpenWrite(binPath)))
        {
            foreach (float d in result.FinalPhysicalDensity)
            {
                bw.Write(d);
            }
        }

        Console.WriteLine("-----+------------+---------+--------+--------+----------------------+----------");
        Console.WriteLine($"Optimization complete in {result.TotalIterations} iterations.");
        Console.WriteLine($"Final Compliance: {result.FinalCompliance:F4}, Final Volume: {result.FinalVolumeFraction:P2}");
        Console.WriteLine($"Artifacts saved to {outDir}: metrics.csv, density.bin");
        return 0;
    }

    private static (FeModel Model, OptimizerOptions Options) LoadModel(string jsonPath)
    {
        string json = File.ReadAllText(jsonPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        float voxelSize = root.TryGetProperty("voxelSize", out var vsProp) ? vsProp.GetSingle() : 1.0f;
        double e0 = 3000.0, nu = 0.3, eminRatio = 1e-6;
        if (root.TryGetProperty("material", out var matProp))
        {
            if (matProp.TryGetProperty("e0", out var e0P)) e0 = e0P.GetDouble();
            if (matProp.TryGetProperty("nu", out var nuP)) nu = nuP.GetDouble();
            if (matProp.TryGetProperty("eminRatio", out var emP)) eminRatio = emP.GetDouble();
        }
        var material = new FeMaterial(e0, nu, eminRatio);

        GridSpec grid;
        byte[] domain;

        if (root.TryGetProperty("stl", out var stlProp) && !string.IsNullOrWhiteSpace(stlProp.GetString()))
        {
            string stlPath = Path.IsPathRooted(stlProp.GetString()!) ? stlProp.GetString()! : Path.Combine(Path.GetDirectoryName(jsonPath) ?? "", stlProp.GetString()!);
            using var fs = File.OpenRead(stlPath);
            var (mesh, _) = StlReader.Read(fs);
            var (min, max) = mesh.BBox;
            int nx = (int)Math.Ceiling((max.X - min.X) / voxelSize);
            int ny = (int)Math.Ceiling((max.Y - min.Y) / voxelSize);
            int nz = (int)Math.Ceiling((max.Z - min.Z) / voxelSize);
            grid = new GridSpec(min, voxelSize, nx, ny, nz);
            var (vm, _) = Voxeliser.Voxelise(mesh, grid);
            domain = vm.Domain;
        }
        else
        {
            var box = root.GetProperty("box");
            int nx = box[0].GetInt32(), ny = box[1].GetInt32(), nz = box[2].GetInt32();
            grid = new GridSpec(Vector3.Zero, voxelSize, nx, ny, nz);
            domain = new byte[nx * ny * nz];
            Array.Fill(domain, (byte)1);
        }

        // Parse Supports
        var fixedDofsList = new List<int>();
        if (root.TryGetProperty("supports", out var supArr))
        {
            foreach (var sup in supArr.EnumerateArray())
            {
                var b = sup.GetProperty("box");
                float minX = b[0].GetSingle(), minY = b[1].GetSingle(), minZ = b[2].GetSingle();
                float maxX = b[3].GetSingle(), maxY = b[4].GetSingle(), maxZ = b[5].GetSingle();
                bool fixX = !sup.TryGetProperty("fixX", out var fx) || fx.GetBoolean();
                bool fixY = !sup.TryGetProperty("fixY", out var fy) || fy.GetBoolean();
                bool fixZ = !sup.TryGetProperty("fixZ", out var fz) || fz.GetBoolean();

                for (int k = 0; k <= grid.Nz; k++)
                    for (int j = 0; j <= grid.Ny; j++)
                        for (int i = 0; i <= grid.Nx; i++)
                        {
                            var pt = grid.NodePosition(i, j, k);
                            if (pt.X >= minX && pt.X <= maxX && pt.Y >= minY && pt.Y <= maxY && pt.Z >= minZ && pt.Z <= maxZ)
                            {
                                int n = (int)grid.NodeIndex(i, j, k);
                                if (fixX) fixedDofsList.Add(3 * n + 0);
                                if (fixY) fixedDofsList.Add(3 * n + 1);
                                if (fixZ) fixedDofsList.Add(3 * n + 2);
                            }
                        }
            }
        }

        // Parse Loads
        var loads = new List<LoadCase>();
        if (root.TryGetProperty("loads", out var loadArr))
        {
            foreach (var ld in loadArr.EnumerateArray())
            {
                var b = ld.GetProperty("box");
                float minX = b[0].GetSingle(), minY = b[1].GetSingle(), minZ = b[2].GetSingle();
                float maxX = b[3].GetSingle(), maxY = b[4].GetSingle(), maxZ = b[5].GetSingle();
                var f = ld.GetProperty("force");
                var forceVec = new Vector3(f[0].GetSingle(), f[1].GetSingle(), f[2].GetSingle());

                var patchNodes = new List<int>();
                var patchAreas = new List<double>();
                for (int k = 0; k <= grid.Nz; k++)
                    for (int j = 0; j <= grid.Ny; j++)
                        for (int i = 0; i <= grid.Nx; i++)
                        {
                            var pt = grid.NodePosition(i, j, k);
                            if (pt.X >= minX && pt.X <= maxX && pt.Y >= minY && pt.Y <= maxY && pt.Z >= minZ && pt.Z <= maxZ)
                            {
                                patchNodes.Add((int)grid.NodeIndex(i, j, k));
                                patchAreas.Add(voxelSize * voxelSize);
                            }
                        }

                var patch = new SurfacePatch(
                    Array.Empty<VoxelFace>(),
                    patchNodes.ToArray(),
                    patchAreas.ToArray(),
                    (new Vector3(minX, minY, minZ), new Vector3(maxX, maxY, maxZ)),
                    true);
                loads.Add(new LoadCase(patch, forceVec));
            }
        }

        // Parse Preserve (passive solid)
        byte[]? passiveMask = null;
        if (root.TryGetProperty("preserve", out var presArr))
        {
            passiveMask = new byte[grid.TotalElements];
            foreach (var p in presArr.EnumerateArray())
            {
                var b = p.GetProperty("box");
                float minX = b[0].GetSingle(), minY = b[1].GetSingle(), minZ = b[2].GetSingle();
                float maxX = b[3].GetSingle(), maxY = b[4].GetSingle(), maxZ = b[5].GetSingle();

                for (int iz = 0; iz < grid.Nz; iz++)
                    for (int iy = 0; iy < grid.Ny; iy++)
                        for (int ix = 0; ix < grid.Nx; ix++)
                        {
                            var pt = grid.ElementCenter(ix, iy, iz);
                            if (pt.X >= minX && pt.X <= maxX && pt.Y >= minY && pt.Y <= maxY && pt.Z >= minZ && pt.Z <= maxZ)
                            {
                                int e = (int)grid.ElementIndex(ix, iy, iz);
                                passiveMask[e] = 1;
                            }
                        }
            }
        }

        var model = new FeModel(grid, domain, material, fixedDofsList.ToArray(), loads);

        // Parse Options
        double volFrac = 0.4, filterRadius = 2.0, moveLimit = 0.2, penal = 3.0;
        int maxIter = 100;
        if (root.TryGetProperty("options", out var optProp))
        {
            if (optProp.TryGetProperty("volumeFraction", out var vfP)) volFrac = vfP.GetDouble();
            if (optProp.TryGetProperty("filterRadiusMm", out var frP)) filterRadius = frP.GetDouble();
            if (optProp.TryGetProperty("maxIterations", out var miP)) maxIter = miP.GetInt32();
            if (optProp.TryGetProperty("moveLimit", out var mlP)) moveLimit = mlP.GetDouble();
            if (optProp.TryGetProperty("penal", out var penP)) penal = penP.GetDouble();
        }

        var options = new OptimizerOptions(
            VolumeFraction: volFrac,
            FilterRadiusMm: filterRadius,
            MaxIterations: maxIter,
            MoveLimit: moveLimit,
            Penal: penal,
            EminRatio: eminRatio,
            PassiveMask: passiveMask);

        return (model, options);
    }

    private static int RunFemBenchmarks()
    {
        Console.WriteLine("Running FEM solver performance benchmarks...");
        var grid = new GridSpec(Vector3.Zero, 1.0f, 40, 20, 4);
        byte[] domain = new byte[40 * 20 * 4];
        Array.Fill(domain, (byte)1);
        var mat = new FeMaterial(3000.0, 0.3, 1e-6);
        var fixedNodes = new List<int>();
        for (int k = 0; k <= 4; k++)
            for (int j = 0; j <= 20; j++)
                fixedNodes.Add((int)grid.NodeIndex(0, j, k));
        int[] fixedDofs = FeModel.FixDofs(fixedNodes, true, true, true);
        var tipNodes = new int[] { (int)grid.NodeIndex(40, 10, 2) };
        var patch = new SurfacePatch(Array.Empty<VoxelFace>(), tipNodes, new double[] { 1.0 }, (new Vector3(40, 0, 0), new Vector3(40, 20, 4)), true);
        var model = new FeModel(grid, domain, mat, fixedDofs, new[] { new LoadCase(patch, new Vector3(0, 0, -1000.0f)) });

        var sw = Stopwatch.StartNew();
        var res = StaticAnalysis.Run(model, new StaticAnalysisOptions(Tolerance: 1e-6));
        sw.Stop();
        Console.WriteLine($"Solve complete: {res.Compliance:F2} in {sw.Elapsed.TotalMilliseconds:F1} ms ({res.SolverInfo.Iterations} iters)");
        return 0;
    }
}
