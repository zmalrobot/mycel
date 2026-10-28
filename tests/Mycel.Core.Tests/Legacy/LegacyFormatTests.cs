using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Mycel.Core.Geometry;
using Mycel.Core.Persistence;
using Mycel.Core.Workflow;
using Mycel.Core.Tests.Fixtures;
using Xunit;

namespace Mycel.Core.Tests.Legacy;

/// <summary>
/// Verifies that projects and runs written by the application before the rename to Mycel
/// (product name "TopoOpt") still open unchanged. The old name is deliberately present in these
/// fixtures: the readers must not validate any application name, and the binary magic "TOPO"
/// of iteration files must stay byte-for-byte identical.
/// </summary>
public sealed class LegacyFormatTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Mycel_LegacyTests_" + Guid.NewGuid().ToString("N"));

    public LegacyFormatTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void OldFormatProject_WithLegacyProductName_StillOpens()
    {
        string projectDir = Path.Combine(_root, "old_project");
        Directory.CreateDirectory(projectDir);

        // model.stl with the legacy STL header text.
        using (var ms = new MemoryStream())
        {
            StlFixtures.WriteBinary(ms, StlFixtures.Box(new Vector3(10, 10, 10)));
            byte[] bytes = ms.ToArray();
            byte[] legacyHeader = Encoding.ASCII.GetBytes("TopoOpt Binary STL (mm)");
            Array.Clear(bytes, 0, 80);
            legacyHeader.CopyTo(bytes, 0);
            File.WriteAllBytes(Path.Combine(projectDir, "model.stl"), bytes);
        }

        string hash = ProjectSerializer.ComputeSha256(Path.Combine(projectDir, "model.stl"));
        string json = $$"""
        {
          "application": "TopoOpt",
          "version": 1,
          "modelFile": "model.stl",
          "stlSha256": "{{hash}}",
          "unit": "Millimeter",
          "loads": [],
          "supports": [],
          "preserveRegions": [],
          "material": { "presetKind": "Pla", "e": 3000, "nu": 0.36, "yieldXy": 50, "yieldZ": 30, "conservativeZ": true, "density": 1.24 },
          "resolution": { "preset": "Standard", "targetSolidElements": 100000, "explicitH": 0.0 }
        }
        """;
        File.WriteAllText(Path.Combine(projectDir, "project.json"), json);

        var state = ProjectSerializer.Open(projectDir);

        Assert.NotNull(state.Mesh);
        Assert.Equal(12, state.Mesh!.TriangleCount);
        Assert.Equal(hash, state.StlSha256);
        Assert.Equal(LengthUnit.Millimeter, state.Unit);
    }

    [Fact]
    public void OldFormatRun_WithTopoMagicIterationFile_StillReads()
    {
        string runDir = Path.Combine(_root, "old_run");
        string iterDir = Path.Combine(runDir, "iterations");
        Directory.CreateDirectory(iterDir);

        const int nx = 2, ny = 2, nz = 2;
        var metadata = new RunMetadata
        {
            RunName = "legacy_TopoOpt_run",
            AppVersion = "0.1.3",
            Grid = new GridSpecDto(0f, 0f, 0f, 1.0, nx, ny, nz),
            Status = RunStatus.Completed,
            CompletedIterations = 1
        };
        File.WriteAllText(Path.Combine(runDir, "run.json"), JsonSerializer.Serialize(metadata));

        // Iteration file in the pre-rename on-disk layout: 72-byte header with the literal magic "TOPO".
        byte[] quantized = new byte[nx * ny * nz];
        for (int i = 0; i < quantized.Length; i++) quantized[i] = (byte)(i * 30 + 5);

        uint checksum = 2166136261u;
        foreach (byte q in quantized)
        {
            checksum ^= q;
            checksum *= 16777619u;
        }

        byte[] header = new byte[72];
        "TOPO"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), nx);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), ny);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16), nz);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(20), 1);
        BinaryPrimitives.WriteDoubleLittleEndian(header.AsSpan(24), 12.5);
        BinaryPrimitives.WriteDoubleLittleEndian(header.AsSpan(32), 0.4);
        BinaryPrimitives.WriteDoubleLittleEndian(header.AsSpan(40), 0.01);
        BinaryPrimitives.WriteDoubleLittleEndian(header.AsSpan(48), 5.0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(56), 17);
        BinaryPrimitives.WriteDoubleLittleEndian(header.AsSpan(60), 3.5);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(68), checksum);

        using (var fs = File.Create(Path.Combine(iterDir, "it_000001.bin.br")))
        {
            fs.Write(header);
            using var brotli = new BrotliStream(fs, CompressionLevel.Fastest);
            brotli.Write(quantized);
        }

        var reader = RunReader.Open(runDir);
        var (hdr, density) = reader.ReadIteration(1);

        Assert.Equal("legacy_TopoOpt_run", reader.Metadata.RunName);
        Assert.Equal(1, hdr.Iteration);
        Assert.Equal(12.5, hdr.Compliance);
        Assert.Equal(17, hdr.SolverIterations);
        Assert.Equal(nx * ny * nz, density.Length);
        Assert.Equal(quantized[3] / 255.0f, density[3]);
    }
}

