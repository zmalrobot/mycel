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
        using (var bw = new BinaryWriter(File.Create(binPath)))
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

        float voxelSize = 1.0f;
        if (root.TryGetProperty("resolution", out var resProp)) voxelSize = resProp.GetSingle();
        else if (root.TryGetProperty("voxelSize", out var vsProp)) voxelSize = vsProp.GetSingle();

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
        IndexedMesh? mesh = null;
        VoxelModel? vm = null;

        if (root.TryGetProperty("stl", out var stlProp) && !string.IsNullOrWhiteSpace(stlProp.GetString()))
        {
            string stlPath = Path.IsPathRooted(stlProp.GetString()!) ? stlProp.GetString()! : Path.Combine(Path.GetDirectoryName(jsonPath) ?? "", stlProp.GetString()!);
            using var fs = File.OpenRead(stlPath);
            var (readMesh, _) = StlReader.Read(fs);
            mesh = readMesh;
            var (min, max) = mesh.BBox;
            int nx = (int)Math.Ceiling((max.X - min.X) / voxelSize);
            int ny = (int)Math.Ceiling((max.Y - min.Y) / voxelSize);
            int nz = (int)Math.Ceiling((max.Z - min.Z) / voxelSize);
            grid = new GridSpec(min, voxelSize, nx, ny, nz);
            var (voxModel, _) = Voxeliser.Voxelise(mesh, grid);
            vm = voxModel;
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

        static int[]? ParseTriangles(JsonElement elem)
        {
            if (elem.TryGetProperty("triangles", out var tArr))
            {
                var list = new List<int>();
                foreach (var item in tArr.EnumerateArray()) list.Add(item.GetInt32());
                return list.ToArray();
            }
            if (elem.TryGetProperty("triangleRange", out var trArr))
            {
                int start = trArr[0].GetInt32(), count = trArr[1].GetInt32();
                var arr = new int[count];
                for (int i = 0; i < count; i++) arr[i] = start + i;
                return arr;
            }
            return null;
        }

        // Parse Supports
        var fixedDofsList = new List<int>();
        if (root.TryGetProperty("supports", out var supArr))
        {
            foreach (var sup in supArr.EnumerateArray())
            {
                bool fixX = !sup.TryGetProperty("fixX", out var fx) || fx.GetBoolean();
                bool fixY = !sup.TryGetProperty("fixY", out var fy) || fy.GetBoolean();
                bool fixZ = !sup.TryGetProperty("fixZ", out var fz) || fz.GetBoolean();

                var triIndices = ParseTriangles(sup);
                if (triIndices != null && mesh != null && vm != null)
                {
                    var patch = SurfacePatchBuilder.Build(vm, mesh, triIndices);
                    foreach (int n in patch.Nodes)
                    {
                        if (fixX) fixedDofsList.Add(3 * n + 0);
                        if (fixY) fixedDofsList.Add(3 * n + 1);
                        if (fixZ) fixedDofsList.Add(3 * n + 2);
                    }
                }
                else if (sup.TryGetProperty("box", out var b))
                {
                    foreach (int n in NodesInBox(grid, b))
                    {
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
                var f = ld.GetProperty("force");
                var forceVec = new Vector3(f[0].GetSingle(), f[1].GetSingle(), f[2].GetSingle());

                var triIndices = ParseTriangles(ld);
                if (triIndices != null && mesh != null && vm != null)
                {
                    var patch = SurfacePatchBuilder.Build(vm, mesh, triIndices);
                    loads.Add(new LoadCase(patch, forceVec));
                }
                else if (ld.TryGetProperty("box", out var b))
                {
                    var pNodes = NodesInBox(grid, b);
                    var pAreas = new double[pNodes.Count];
                    Array.Fill(pAreas, voxelSize * voxelSize);
                    var patch = new SurfacePatch(
                        Array.Empty<VoxelFace>(),
                        pNodes.ToArray(),
                        pAreas,
                        (new Vector3(b[0].GetSingle(), b[1].GetSingle(), b[2].GetSingle()),
                         new Vector3(b[3].GetSingle(), b[4].GetSingle(), b[5].GetSingle())),
                        true);
                    loads.Add(new LoadCase(patch, forceVec));
                }
            }
        }

        // Parse Preserve (passive solid)
        byte[]? passiveMask = null;
        if (root.TryGetProperty("preserve", out var presArr))
        {
            passiveMask = new byte[grid.TotalElements];
            foreach (var p in presArr.EnumerateArray())
            {
                var triIndices = ParseTriangles(p);
                if (triIndices != null && mesh != null && vm != null)
                {
                    var patch = SurfacePatchBuilder.Build(vm, mesh, triIndices);
                    var shell = PassiveRegion.Build(grid, null, patch.Nodes, radiusInVoxels: 1);
                    for (int i = 0; i < grid.TotalElements; i++)
                    {
                        if (shell[i] != 0) passiveMask[i] = 1;
                    }
                }
                else if (p.TryGetProperty("box", out var b))
                {
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

    private static List<int> NodesInBox(GridSpec g, JsonElement b)
    {
        float x0 = b[0].GetSingle(), y0 = b[1].GetSingle(), z0 = b[2].GetSingle();
        float x1 = b[3].GetSingle(), y1 = b[4].GetSingle(), z1 = b[5].GetSingle();
        var nodes = new List<int>();
        for (int k = 0; k <= g.Nz; k++)
            for (int j = 0; j <= g.Ny; j++)
                for (int i = 0; i <= g.Nx; i++)
                {
                    var pt = g.NodePosition(i, j, k);
                    if (pt.X >= x0 && pt.X <= x1 && pt.Y >= y0 && pt.Y <= y1 && pt.Z >= z0 && pt.Z <= z1)
                        nodes.Add((int)g.NodeIndex(i, j, k));
                }
        return nodes;
    }
}
