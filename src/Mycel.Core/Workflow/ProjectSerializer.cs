using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mycel.Core.Geometry;

namespace Mycel.Core.Workflow;

public static class ProjectSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(stream);
        return Convert.ToHexStringLower(hash);
    }

    public static string ComputeSha256(ReadOnlySpan<byte> bytes)
    {
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(bytes.ToArray());
        return Convert.ToHexStringLower(hash);
    }

    public static int[][] EncodeRle(int[] indices)
    {
        if (indices == null || indices.Length == 0)
            return Array.Empty<int[]>();

        int[] sorted = (int[])indices.Clone();
        Array.Sort(sorted);

        var ranges = new List<int[]>();
        int start = sorted[0];
        int count = 1;

        for (int i = 1; i < sorted.Length; i++)
        {
            if (sorted[i] == sorted[i - 1])
                continue; // Skip duplicates

            if (sorted[i] == sorted[i - 1] + 1)
            {
                count++;
            }
            else
            {
                ranges.Add(new[] { start, count });
                start = sorted[i];
                count = 1;
            }
        }
        ranges.Add(new[] { start, count });

        return ranges.ToArray();
    }

    public static int[] DecodeRle(int[][] ranges)
    {
        if (ranges == null || ranges.Length == 0)
            return Array.Empty<int>();

        int totalCount = 0;
        for (int i = 0; i < ranges.Length; i++)
        {
            if (ranges[i].Length >= 2)
                totalCount += ranges[i][1];
        }

        int[] result = new int[totalCount];
        int destIdx = 0;

        for (int i = 0; i < ranges.Length; i++)
        {
            if (ranges[i].Length >= 2)
            {
                int start = ranges[i][0];
                int count = ranges[i][1];
                for (int c = 0; c < count; c++)
                {
                    result[destIdx++] = start + c;
                }
            }
        }

        return result;
    }

    public static void Save(string projectDir, ProjectState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(projectDir))
            throw new ArgumentException("Project directory must be specified.", nameof(projectDir));

        Directory.CreateDirectory(projectDir);
        string modelPath = Path.Combine(projectDir, "model.stl");

        // Copy or write model.stl if not already there
        if (state.Mesh != null)
        {
            if (!File.Exists(modelPath) || (state.FilePath != null && Path.GetFullPath(state.FilePath) != Path.GetFullPath(modelPath)))
            {
                if (state.FilePath != null && File.Exists(state.FilePath))
                {
                    File.Copy(state.FilePath, modelPath, overwrite: true);
                }
                else
                {
                    using var outFs = File.Create(modelPath);
                    StlWriter.WriteBinary(outFs, state.Mesh);
                }
            }
        }

        string hash = File.Exists(modelPath) ? ComputeSha256(modelPath) : (state.StlSha256 ?? string.Empty);
        state.StlSha256 = hash;

        var dto = new ProjectJsonDto
        {
            Version = 1,
            ModelFile = "model.stl",
            StlSha256 = hash,
            Unit = state.Unit.ToString(),
            Loads = state.Loads.ConvertAll(l => new LoadRegionDto
            {
                Id = l.Id,
                Name = l.Name,
                TrianglesRle = EncodeRle(l.Triangles),
                MagnitudeNewtons = l.MagnitudeNewtons,
                DirectionMode = l.DirectionMode.ToString(),
                CustomDirection = new[] { l.CustomDirection.X, l.CustomDirection.Y, l.CustomDirection.Z }
            }),
            Supports = state.Supports.ConvertAll(s => new SupportRegionDto
            {
                Id = s.Id,
                Name = s.Name,
                TrianglesRle = EncodeRle(s.Triangles),
                FixX = s.FixX,
                FixY = s.FixY,
                FixZ = s.FixZ
            }),
            PreserveRegions = state.PreserveRegions.ConvertAll(p => new PreserveRegionDto
            {
                Id = p.Id,
                Name = p.Name,
                TrianglesRle = EncodeRle(p.Triangles)
            }),
            Material = new MaterialDto
            {
                PresetKind = state.Material.PresetKind.ToString(),
                E = state.Material.E,
                Nu = state.Material.Nu,
                YieldXy = state.Material.YieldXy,
                YieldZ = state.Material.YieldZ,
                ConservativeZ = state.Material.ConservativeZ,
                Density = state.Material.Density,
                InfillPercent = state.Material.InfillPercent,
                InfillPattern = state.Material.InfillPattern.ToString()
            },
            Resolution = new ResolutionDto
            {
                Preset = state.Resolution.Preset.ToString(),
                TargetSolidElements = state.Resolution.TargetSolidElements,
                ExplicitH = state.Resolution.ExplicitH
            }
        };

        string json = JsonSerializer.Serialize(dto, JsonOptions);
        string jsonPath = Path.Combine(projectDir, "project.json");
        File.WriteAllText(jsonPath, json);
    }

    public static ProjectState Open(string projectDirOrJson)
    {
        if (string.IsNullOrWhiteSpace(projectDirOrJson))
            throw new ArgumentException("Path must be specified.", nameof(projectDirOrJson));

        string projectDir = Directory.Exists(projectDirOrJson)
            ? projectDirOrJson
            : (Path.GetDirectoryName(projectDirOrJson) ?? ".");

        string jsonPath = Path.Combine(projectDir, "project.json");
        if (!File.Exists(jsonPath))
            throw new FileNotFoundException($"project.json not found in '{projectDir}'.", jsonPath);

        string json = File.ReadAllText(jsonPath);
        var dto = JsonSerializer.Deserialize<ProjectJsonDto>(json, JsonOptions)
                  ?? throw new InvalidDataException("Failed to deserialize project.json.");

        string modelPath = Path.Combine(projectDir, dto.ModelFile ?? "model.stl");
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Model file '{modelPath}' not found in project directory.", modelPath);

        // Verify SHA-256
        string actualHash = ComputeSha256(modelPath);
        if (!string.Equals(actualHash, dto.StlSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Model STL SHA-256 hash mismatch! Expected '{dto.StlSha256}', but computed '{actualHash}'. " +
                "The STL file may have been modified, corrupting triangle index selections.");
        }

        // Load mesh
        IndexedMesh mesh;
        MeshReport report;
        using (var stream = File.OpenRead(modelPath))
        {
            (mesh, report) = StlReader.Read(stream);
        }

        var state = new ProjectState
        {
            Mesh = mesh,
            MeshReport = report,
            FilePath = modelPath,
            StlSha256 = actualHash,
            Unit = Enum.TryParse<LengthUnit>(dto.Unit, out var unit) ? unit : LengthUnit.Millimeter
        };

        // Material
        if (dto.Material != null)
        {
            state.Material = new MaterialDefinition
            {
                PresetKind = Enum.TryParse<MaterialPresetKind>(dto.Material.PresetKind, out var mpk) ? mpk : MaterialPresetKind.Pla,
                E = dto.Material.E,
                Nu = dto.Material.Nu,
                YieldXy = dto.Material.YieldXy,
                YieldZ = dto.Material.YieldZ,
                ConservativeZ = dto.Material.ConservativeZ,
                Density = dto.Material.Density > 0.01 ? dto.Material.Density : 1.24,
                InfillPercent = dto.Material.InfillPercent > 0.0 ? dto.Material.InfillPercent : 100.0,
                InfillPattern = Enum.TryParse<InfillPatternKind>(dto.Material.InfillPattern, out var ipk) ? ipk : InfillPatternKind.Gyroid
            };
        }

        // Resolution
        if (dto.Resolution != null)
        {
            state.Resolution = new ResolutionConfig
            {
                Preset = Enum.TryParse<ResolutionPreset>(dto.Resolution.Preset, out var rp) ? rp : ResolutionPreset.Standard,
                TargetSolidElements = dto.Resolution.TargetSolidElements,
                ExplicitH = dto.Resolution.ExplicitH
            };
        }

        // Loads
        if (dto.Loads != null)
        {
            foreach (var ld in dto.Loads)
            {
                var l = new LoadRegion
                {
                    Id = ld.Id != Guid.Empty ? ld.Id : Guid.NewGuid(),
                    Name = ld.Name ?? "Load",
                    Triangles = DecodeRle(ld.TrianglesRle ?? Array.Empty<int[]>()),
                    MagnitudeNewtons = ld.MagnitudeNewtons,
                    DirectionMode = Enum.TryParse<LoadDirectionMode>(ld.DirectionMode, out var dm) ? dm : LoadDirectionMode.SurfaceNormalInward,
                    CustomDirection = ld.CustomDirection != null && ld.CustomDirection.Length == 3
                        ? new Vector3(ld.CustomDirection[0], ld.CustomDirection[1], ld.CustomDirection[2])
                        : -Vector3.UnitZ
                };
                l.UpdateGeometryMetrics(mesh);
                state.Loads.Add(l);
            }
        }

        // Supports
        if (dto.Supports != null)
        {
            foreach (var sd in dto.Supports)
            {
                var s = new SupportRegion
                {
                    Id = sd.Id != Guid.Empty ? sd.Id : Guid.NewGuid(),
                    Name = sd.Name ?? "Support",
                    Triangles = DecodeRle(sd.TrianglesRle ?? Array.Empty<int[]>()),
                    FixX = sd.FixX,
                    FixY = sd.FixY,
                    FixZ = sd.FixZ
                };
                s.UpdateGeometryMetrics(mesh);
                state.Supports.Add(s);
            }
        }

        // Preserve
        if (dto.PreserveRegions != null)
        {
            foreach (var pd in dto.PreserveRegions)
            {
                var p = new PreserveRegion
                {
                    Id = pd.Id != Guid.Empty ? pd.Id : Guid.NewGuid(),
                    Name = pd.Name ?? "Preserve",
                    Triangles = DecodeRle(pd.TrianglesRle ?? Array.Empty<int[]>())
                };
                p.UpdateGeometryMetrics(mesh);
                state.PreserveRegions.Add(p);
            }
        }

        return state;
    }

    private sealed class ProjectJsonDto
    {
        public int Version { get; set; } = 1;
        public string? ModelFile { get; set; } = "model.stl";
        public string? StlSha256 { get; set; }
        public string? Unit { get; set; }
        public List<LoadRegionDto>? Loads { get; set; }
        public List<SupportRegionDto>? Supports { get; set; }
        public List<PreserveRegionDto>? PreserveRegions { get; set; }
        public MaterialDto? Material { get; set; }
        public ResolutionDto? Resolution { get; set; }
    }

    private sealed class LoadRegionDto
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public int[][]? TrianglesRle { get; set; }
        public float MagnitudeNewtons { get; set; }
        public string? DirectionMode { get; set; }
        public float[]? CustomDirection { get; set; }
    }

    private sealed class SupportRegionDto
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public int[][]? TrianglesRle { get; set; }
        public bool FixX { get; set; } = true;
        public bool FixY { get; set; } = true;
        public bool FixZ { get; set; } = true;
    }

    private sealed class PreserveRegionDto
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public int[][]? TrianglesRle { get; set; }
    }

    private sealed class MaterialDto
    {
        public string? PresetKind { get; set; }
        public double E { get; set; }
        public double Nu { get; set; }
        public double YieldXy { get; set; }
        public double YieldZ { get; set; }
        public bool ConservativeZ { get; set; }
        public double Density { get; set; } = 1.24;
        public double InfillPercent { get; set; } = 100.0;
        public string? InfillPattern { get; set; } = "Gyroid";
    }

    private sealed class ResolutionDto
    {
        public string? Preset { get; set; }
        public int TargetSolidElements { get; set; }
        public float ExplicitH { get; set; }
    }
}
