using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using TopoOpt.Core.Geometry;

namespace TopoOpt.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--benchmark-1m")
        {
            RunOneMillionTriangleBenchmark();
            return 0;
        }

        if (args.Length > 0 && File.Exists(args[0]))
        {
            string path = args[0];
            using FileStream fs = File.OpenRead(path);
            var (mesh, report) = StlReader.Read(fs);
            Console.WriteLine($"Loaded: {Path.GetFileName(path)}");
            Console.WriteLine($"  Triangles:        {report.Triangles:N0}");
            Console.WriteLine($"  Vertices:         {report.Vertices:N0} ({report.VerticesMerged:N0} merged)");
            Console.WriteLine($"  Degenerate skip:  {report.DegenerateSkipped:N0}");
            Console.WriteLine($"  Open edges:       {report.OpenEdges:N0}");
            Console.WriteLine($"  Non-manifold:     {report.NonManifoldEdges:N0}");
            Console.WriteLine($"  Watertight:       {report.IsWatertight}");
            Console.WriteLine($"  Volume:           {report.Volume:F3} mm³");
            Console.WriteLine($"  BBox Min:         {report.BBox.Min}");
            Console.WriteLine($"  BBox Max:         {report.BBox.Max}");
            Console.WriteLine($"  Load time:        {report.LoadDuration.TotalMilliseconds:F1} ms");
            return 0;
        }

        if (args.Length > 0 && args[0] == "--benchmark-fem")
        {
            RunFemBenchmark();
            return 0;
        }

        if (args.Length > 0 && args[0] == "--cantilever-table")
        {
            RunCantileverConvergenceTable();
            return 0;
        }

        Console.WriteLine("TopoOpt CLI — Usage:");
        Console.WriteLine("  TopoOpt.Cli <file.stl>         Inspect STL file and topology report");
        Console.WriteLine("  TopoOpt.Cli --benchmark-1m     Run 1M-triangle STL + BVH + Picking benchmark");
        Console.WriteLine("  TopoOpt.Cli --benchmark-fem    Run Step 3 FEM solver benchmarks (V10)");
        return 0;
    }

    private static void RunOneMillionTriangleBenchmark()
    {
        int lat = 708;
        int lon = 708;
        float radius = 50f;
        int expectedTris = 2 * lon * (lat - 1);

        Console.WriteLine($"Generating binary STL in memory for sphere ({lat}x{lon} = {expectedTris:N0} triangles)...");
        using MemoryStream ms = new(84 + expectedTris * 50);
        WriteProceduralSphereBinaryStl(ms, radius, lat, lon);
        ms.Position = 0;

        Stopwatch sw = Stopwatch.StartNew();
        var (mesh, report) = StlReader.Read(ms);
        TimeSpan readTime = sw.Elapsed;

        sw.Restart();
        Bvh bvh = new(mesh);
        TimeSpan bvhTime = sw.Elapsed;

        sw.Restart();
        RegionSelector selector = new(mesh);
        TimeSpan adjTime = sw.Elapsed;

        const int RayCount = 50_000;
        uint state = 0x13579BDFu;
        int hits = 0;

        sw.Restart();
        for (int i = 0; i < RayCount; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            float ox = ((state & 0xFFFFu) / 65535f - 0.5f) * 180f;
            float oy = (((state >> 16) & 0xFFFFu) / 65535f - 0.5f) * 180f;
            Vector3 origin = new(ox, oy, 150f);
            Vector3 dir = Vector3.Normalize(new Vector3(-ox * 0.5f, -oy * 0.5f, -150f));
            if (bvh.RayCast(origin, dir).IsHit)
            {
                hits++;
            }
        }
        TimeSpan rayTime = sw.Elapsed;
        double picksPerSec = RayCount / rayTime.TotalSeconds;

        Console.WriteLine("=== 1M-Triangle Benchmark Results ===");
        Console.WriteLine($"  Triangles:          {mesh.TriangleCount:N0}");
        Console.WriteLine($"  Welded Vertices:    {mesh.VertexCount:N0} (merged {report.VerticesMerged:N0})");
        Console.WriteLine($"  Watertight:         {report.IsWatertight} (open={report.OpenEdges}, nonManifold={report.NonManifoldEdges})");
        Console.WriteLine($"  STL Read + Weld:    {readTime.TotalMilliseconds:F1} ms ({readTime.TotalSeconds:F3} s)");
        Console.WriteLine($"  BVH Build:          {bvhTime.TotalMilliseconds:F1} ms ({bvh.NodeCount:N0} nodes)");
        Console.WriteLine($"  Adjacency Build:    {adjTime.TotalMilliseconds:F1} ms");
        Console.WriteLine($"  Raycast ({RayCount:N0}):   {rayTime.TotalMilliseconds:F1} ms -> {picksPerSec:N0} picks/sec ({hits:N0} hits)");
    }

    private static void WriteProceduralSphereBinaryStl(Stream s, float radius, int latSegments, int lonSegments)
    {
        Vector3 northPole = new(0f, 0f, +radius);
        Vector3 southPole = new(0f, 0f, -radius);

        Vector3[,] rings = new Vector3[latSegments - 1, lonSegments];
        for (int i = 1; i < latSegments; i++)
        {
            double phi = Math.PI * i / latSegments;
            float z = (float)(radius * Math.Cos(phi));
            float rRing = (float)(radius * Math.Sin(phi));

            for (int j = 0; j < lonSegments; j++)
            {
                double theta = 2.0 * Math.PI * j / lonSegments;
                rings[i - 1, j] = new Vector3((float)(rRing * Math.Cos(theta)), (float)(rRing * Math.Sin(theta)), z);
            }
        }

        int totalTris = 2 * lonSegments * (latSegments - 1);
        Span<byte> header = stackalloc byte[80];
        header.Clear();
        s.Write(header);

        Span<byte> countBytes = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(countBytes, (uint)totalTris);
        s.Write(countBytes);

        Span<byte> rec = stackalloc byte[50];
        rec.Clear();

        void WriteTri(Stream stream, Span<byte> buf, Vector3 a, Vector3 b, Vector3 c)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(buf[12..16], a.X);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(buf[16..20], a.Y);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(buf[20..24], a.Z);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(buf[24..28], b.X);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(buf[28..32], b.Y);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(buf[32..36], b.Z);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(buf[36..40], c.X);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(buf[40..44], c.Y);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(buf[44..48], c.Z);
            stream.Write(buf);
        }

        for (int j = 0; j < lonSegments; j++)
        {
            int next = (j + 1) % lonSegments;
            WriteTri(s, rec, northPole, rings[0, j], rings[0, next]);
        }

        for (int i = 0; i < latSegments - 2; i++)
        {
            for (int j = 0; j < lonSegments; j++)
            {
                int next = (j + 1) % lonSegments;
                WriteTri(s, rec, rings[i, j], rings[i + 1, j], rings[i + 1, next]);
                WriteTri(s, rec, rings[i, j], rings[i + 1, next], rings[i, next]);
            }
        }

        int last = latSegments - 2;
        for (int j = 0; j < lonSegments; j++)
        {
            int next = (j + 1) % lonSegments;
            WriteTri(s, rec, southPole, rings[last, next], rings[last, j]);
        }
    }

    private static void RunFemBenchmark()
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("  TopoOpt FEM Solver Benchmark (ROADMAP Step 3 - V10)");
        Console.WriteLine($"  Machine: {Environment.MachineName}, OS: {Environment.OSVersion}, Cores: {Environment.ProcessorCount}");
        Console.WriteLine("================================================================================");

        int[] targetCounts = { 50_000, 100_000, 200_000, 500_000 };

        Console.WriteLine("| Elements | DOFs | Matvec (ms) | Cold 1e-4 (iters / s) | Warm 1e-4 (iters / s) | Cold 1e-6 (iters / s) | Memory (MB) | Gate (Warm 1e-4 <= 3s) |");
        Console.WriteLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |");

        foreach (int target in targetCounts)
        {
            int nz = (int)Math.Round(Math.Cbrt(target / 5.0));
            int ny = nz;
            int nx = 5 * nz;
            int totalActive = nx * ny * nz;

            double h = 1.0;
            var grid = new TopoOpt.Core.Voxelization.GridSpec(Vector3.Zero, (float)h, nx, ny, nz);
            byte[] domain = new byte[totalActive];
            Array.Fill(domain, (byte)1);

            var mat = new TopoOpt.Core.Fem.FeMaterial(3000.0, 0.3);

            var fixedNodes = new List<int>();
            for (int k = 0; k <= nz; k++)
                for (int j = 0; j <= ny; j++)
                    fixedNodes.Add((int)grid.NodeIndex(0, j, k));

            int[] fixedDofs = TopoOpt.Core.Fem.FeModel.FixDofs(fixedNodes, true, true, true);

            var tipNodes = new List<int>();
            var tipAreas = new List<double>();
            for (int k = 0; k <= nz; k++)
            {
                for (int j = 0; j <= ny; j++)
                {
                    tipNodes.Add((int)grid.NodeIndex(nx, j, k));
                    tipAreas.Add(h * h);
                }
            }

            var tipPatch = new TopoOpt.Core.Voxelization.SurfacePatch(
                Array.Empty<TopoOpt.Core.Voxelization.VoxelFace>(),
                tipNodes.ToArray(),
                tipAreas.ToArray(),
                (new Vector3(nx * (float)h, 0, 0), new Vector3(nx * (float)h, ny * (float)h, nz * (float)h)),
                true);

            var model = new TopoOpt.Core.Fem.FeModel(grid, domain, mat, fixedDofs, new[] { new TopoOpt.Core.Fem.LoadCase(tipPatch, new Vector3(0, 0, -1000.0f)) });
            var op = new TopoOpt.Core.Fem.FemOperator(model);

            // Benchmark matvec
            double[] x = new double[model.TotalDofs];
            double[] y = new double[model.TotalDofs];
            Array.Fill(x, 1.0);

            // Warmup matvec
            op.ApplyK(x, y, parallel: true);

            int matvecReps = 20;
            var swMatvec = Stopwatch.StartNew();
            for (int r = 0; r < matvecReps; r++)
                op.ApplyK(x, y, parallel: true);
            swMatvec.Stop();
            double msPerMatvec = swMatvec.Elapsed.TotalMilliseconds / matvecReps;

            // Cold solve to 1e-4
            var swCold4 = Stopwatch.StartNew();
            var resCold4 = TopoOpt.Core.Fem.StaticAnalysis.Run(model, new TopoOpt.Core.Fem.StaticAnalysisOptions(Tolerance: 1e-4));
            swCold4.Stop();

            // Realistic warm solve to 1e-4: perturb densities by 2% (simulating a SIMP OC iteration step)
            double[] perturbedRho = (double[])model.ElementDensity.Clone();
            for (int e = 0; e < perturbedRho.Length; e++)
            {
                if (domain[e] != 0)
                {
                    // 2% variation
                    perturbedRho[e] = Math.Clamp(perturbedRho[e] * (1.0 - 0.02 * ((e % 7) - 3) / 3.0), 1e-3, 1.0);
                }
            }
            var warmModel = new TopoOpt.Core.Fem.FeModel(grid, domain, mat, fixedDofs, new[] { new TopoOpt.Core.Fem.LoadCase(tipPatch, new Vector3(0, 0, -1000.0f)) }, elementDensity: perturbedRho);

            var swWarm4 = Stopwatch.StartNew();
            var resWarm4 = TopoOpt.Core.Fem.StaticAnalysis.Run(warmModel, new TopoOpt.Core.Fem.StaticAnalysisOptions(Tolerance: 1e-4, WarmStartDisplacements: resCold4.U));
            swWarm4.Stop();

            // Cold solve to 1e-6
            var swCold6 = Stopwatch.StartNew();
            var resCold6 = TopoOpt.Core.Fem.StaticAnalysis.Run(model, new TopoOpt.Core.Fem.StaticAnalysisOptions(Tolerance: 1e-6));
            swCold6.Stop();

            long memBytes = GC.GetTotalMemory(forceFullCollection: false);
            double memMb = memBytes / (1024.0 * 1024.0);

            string gate = (totalActive == 100_000 || (totalActive >= 90_000 && totalActive <= 110_000))
                ? (swWarm4.Elapsed.TotalSeconds <= 3.0 ? "PASS (<= 3s)" : "FAIL (> 3s)")
                : "-";

            Console.WriteLine($"| {totalActive:N0} | {model.TotalDofs:N0} | {msPerMatvec:F2} | {resCold4.SolverInfo.Iterations} it / {swCold4.Elapsed.TotalSeconds:F2}s | {resWarm4.SolverInfo.Iterations} it / {swWarm4.Elapsed.TotalSeconds:F2}s | {resCold6.SolverInfo.Iterations} it / {swCold6.Elapsed.TotalSeconds:F2}s | {memMb:F1} | {gate} |");
        }

        // Thread count speed-up benchmark at 100k elements
        Console.WriteLine("\n### Speed-up vs Thread Count (100k active elements, matvec)");
        Console.WriteLine("| Threads | Matvec (ms) | Speedup vs 1T |");
        Console.WriteLine("| :--- | :--- | :--- |");

        {
            int nz = 27, ny = 27, nx = 135;
            int totalActive = nx * ny * nz; // 98,415 elements (~100k)
            var grid = new TopoOpt.Core.Voxelization.GridSpec(Vector3.Zero, 1.0f, nx, ny, nz);
            byte[] domain = new byte[totalActive];
            Array.Fill(domain, (byte)1);
            var mat = new TopoOpt.Core.Fem.FeMaterial(3000.0, 0.3);
            var model = new TopoOpt.Core.Fem.FeModel(grid, domain, mat, new int[] { 0, 1, 2, 3, 4, 5 });
            var op = new TopoOpt.Core.Fem.FemOperator(model);

            double[] x = new double[model.TotalDofs];
            double[] y = new double[model.TotalDofs];
            Array.Fill(x, 1.0);

            int[] threadCounts = { 1, 2, 4, 8, Environment.ProcessorCount };
            double baseTime1T = 0.0;

            foreach (int tc in threadCounts)
            {
                // Warmup
                op.ApplyK(x, y, parallel: tc > 1, maxDegreeOfParallelism: tc);

                int reps = 15;
                var sw = Stopwatch.StartNew();
                for (int r = 0; r < reps; r++)
                    op.ApplyK(x, y, parallel: tc > 1, maxDegreeOfParallelism: tc);
                sw.Stop();
                double avgMs = sw.Elapsed.TotalMilliseconds / reps;

                if (tc == 1) baseTime1T = avgMs;
                double speedup = baseTime1T / avgMs;

                Console.WriteLine($"| {tc} threads | {avgMs:F2} ms | {speedup:F2}x |");
            }
        }
    }

    private static void RunCantileverConvergenceTable()
    {
        double L = 100.0, b = 10.0, d = 10.0;
        double E = 3000.0, nu = 0.3, P = 1000.0;

        double I = (b * Math.Pow(d, 3)) / 12.0;
        double G = E / (2.0 * (1.0 + nu));
        double kappa = 10.0 * (1.0 + nu) / (12.0 + 11.0 * nu);
        double A = b * d;
        double deltaTimoshenko = (P * Math.Pow(L, 3)) / (3.0 * E * I) + (P * L) / (kappa * G * A);

        Console.WriteLine($"Timoshenko Analytic Deflection: {deltaTimoshenko:F6} mm (Bending: {(P*Math.Pow(L,3))/(3*E*I):F6} mm, Shear: {(P*L)/(kappa*G*A):F6} mm, kappa: {kappa:F6})");
        Console.WriteLine("| Elements Through Depth (Nz) | Mesh (Nx × Ny × Nz) | Voxel Size h (mm) | Total Elements | FE Tip Deflection δ (mm) | Error vs Timoshenko | Monotonic Decrease |");
        Console.WriteLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- |");

        int[] depths = { 1, 2, 4, 8 };
        double prevErr = double.MaxValue;

        foreach (int nz in depths)
        {
            int ny = nz;
            double h = d / nz;
            int nx = (int)Math.Round(L / h);

            var grid = new TopoOpt.Core.Voxelization.GridSpec(Vector3.Zero, (float)h, nx, ny, nz);
            byte[] domain = new byte[nx * ny * nz];
            Array.Fill(domain, (byte)1);

            var mat = new TopoOpt.Core.Fem.FeMaterial(E, nu);

            var fixedNodes = new List<int>();
            for (int k = 0; k <= nz; k++)
                for (int j = 0; j <= ny; j++)
                    fixedNodes.Add((int)grid.NodeIndex(0, j, k));

            int[] fixedDofs = TopoOpt.Core.Fem.FeModel.FixDofs(fixedNodes, true, true, true);

            var tipNodes = new List<int>();
            var tipAreas = new List<double>();
            for (int k = 0; k <= nz; k++)
            {
                for (int j = 0; j <= ny; j++)
                {
                    tipNodes.Add((int)grid.NodeIndex(nx, j, k));
                    double dy = (j == 0 || j == ny) ? 0.5 * h : h;
                    double dz = (k == 0 || k == nz) ? 0.5 * h : h;
                    tipAreas.Add(dy * dz);
                }
            }

            var tipPatch = new TopoOpt.Core.Voxelization.SurfacePatch(
                Array.Empty<TopoOpt.Core.Voxelization.VoxelFace>(),
                tipNodes.ToArray(),
                tipAreas.ToArray(),
                (new Vector3((float)L, 0, 0), new Vector3((float)L, (float)b, (float)d)),
                true);

            var model = new TopoOpt.Core.Fem.FeModel(grid, domain, mat, fixedDofs, new[] { new TopoOpt.Core.Fem.LoadCase(tipPatch, new Vector3(0, 0, (float)-P)) });

            var result = TopoOpt.Core.Fem.StaticAnalysis.Run(model, new TopoOpt.Core.Fem.StaticAnalysisOptions(Tolerance: 1e-8, MaxIterations: 10000));

            double sumTipUz = 0.0;
            double sumArea = 0.0;
            for (int i = 0; i < tipNodes.Count; i++)
            {
                int node = tipNodes[i];
                double area = tipAreas[i];
                sumTipUz += Math.Abs(result.U[3 * node + 2]) * area;
                sumArea += area;
            }
            double deltaFe = sumTipUz / sumArea;
            double err = Math.Abs(deltaFe - deltaTimoshenko) / deltaTimoshenko;
            string mono = err < prevErr ? "YES" : "NO";
            prevErr = err;

            Console.WriteLine($"| {nz} | {nx} × {ny} × {nz} | {h:F2} | {nx * ny * nz:N0} | {deltaFe:F4} | {err:P2} | {mono} |");
        }
    }
}

