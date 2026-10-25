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

        Console.WriteLine("TopoOpt CLI — Usage:");
        Console.WriteLine("  TopoOpt.Cli <file.stl>         Inspect STL file and topology report");
        Console.WriteLine("  TopoOpt.Cli --benchmark-1m     Run 1M-triangle STL + BVH + Picking benchmark");
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
}

