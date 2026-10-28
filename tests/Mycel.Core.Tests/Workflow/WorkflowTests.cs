using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Mycel.Core.Fem;
using Mycel.Core.Geometry;
using Mycel.Core.Tests.Fixtures;
using Mycel.Core.Voxelization;
using Mycel.Core.Workflow;
using Xunit;

namespace Mycel.Core.Tests.Workflow;

public class WorkflowTests
{
    [Fact]
    public void Test_DirectionMaths()
    {
        // 1. Box mesh
        var boxTris = StlFixtures.Box(new Vector3(10f, 10f, 10f));
        var mesh = IndexedMeshFromTriangles(boxTris);

        var load = new LoadRegion
        {
            MagnitudeNewtons = 500f,
            DirectionMode = LoadDirectionMode.SurfaceNormalInward
        };

        // Find triangles on +Z face (normal ~= (0, 0, 1))
        var topTriangles = new List<int>();
        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            Vector3 n = mesh.TriangleNormals.Span[i];
            if (Vector3.Dot(n, Vector3.UnitZ) > 0.9f)
            {
                topTriangles.Add(i);
            }
        }
        Assert.True(topTriangles.Count > 0);

        load.Triangles = topTriangles.ToArray();
        load.UpdateGeometryMetrics(mesh);

        // Mean normal should point towards +Z
        Assert.True(Vector3.Dot(load.MeanNormal, Vector3.UnitZ) > 0.99f);

        // Surface normal inward should point towards -Z
        Vector3 inwardDir = load.ComputeUnitDirection();
        Assert.True(Vector3.Dot(inwardDir, -Vector3.UnitZ) > 0.99f);
        Assert.Equal(-500f * Vector3.UnitZ, load.ComputeTotalForce());

        // Surface normal outward should point towards +Z
        load.DirectionMode = LoadDirectionMode.SurfaceNormalOutward;
        Vector3 outwardDir = load.ComputeUnitDirection();
        Assert.True(Vector3.Dot(outwardDir, Vector3.UnitZ) > 0.99f);

        // Axis directions
        load.DirectionMode = LoadDirectionMode.AxisPositiveX;
        Assert.Equal(Vector3.UnitX, load.ComputeUnitDirection());

        // Custom vector normalization
        load.DirectionMode = LoadDirectionMode.CustomVector;
        load.CustomDirection = new Vector3(3f, 4f, 0f);
        Vector3 customUnit = load.ComputeUnitDirection();
        Assert.True(Math.Abs(customUnit.Length() - 1.0f) < 1e-6f);
        Assert.Equal(0.6f, customUnit.X, 1e-5f);
        Assert.Equal(0.8f, customUnit.Y, 1e-5f);
    }

    [Fact]
    public void Test_RleEncodingDecoding()
    {
        // Empty
        Assert.Empty(ProjectSerializer.EncodeRle(Array.Empty<int>()));
        Assert.Empty(ProjectSerializer.DecodeRle(Array.Empty<int[]>()));

        // Consecutive dense range
        int[] dense = Enumerable.Range(10, 50).ToArray();
        var encodedDense = ProjectSerializer.EncodeRle(dense);
        Assert.Single(encodedDense);
        Assert.Equal(10, encodedDense[0][0]);
        Assert.Equal(50, encodedDense[0][1]);
        Assert.Equal(dense, ProjectSerializer.DecodeRle(encodedDense));

        // Mixed sparse and dense with unordered input and duplicates
        int[] mixed = { 5, 2, 3, 1, 10, 11, 12, 2, 20, 21, 22, 23 };
        var encodedMixed = ProjectSerializer.EncodeRle(mixed);
        int[] decoded = ProjectSerializer.DecodeRle(encodedMixed);

        int[] expectedUniqueSorted = { 1, 2, 3, 5, 10, 11, 12, 20, 21, 22, 23 };
        Assert.Equal(expectedUniqueSorted, decoded);
    }

    [Fact]
    public void Test_DirtyTrackingCascade()
    {
        var service = new WorkflowService();
        var boxTris = StlFixtures.Box(new Vector3(20f, 10f, 10f));
        var mesh = IndexedMeshFromTriangles(boxTris);
        service.LoadMesh(mesh);

        // Fake prepared model and analysis result
        var dummyPrep = new PreparedModel(
            CreateDummyFeModel(),
            new VoxelModel(new GridSpec(Vector3.Zero, 1f, 2, 2, 2), new byte[8], 8, 8.0, Array.Empty<ComponentInfo>(), Array.Empty<string>(), Enumerable.Range(0, 8).ToArray()),
            Array.Empty<SurfacePatch>(),
            Array.Empty<SurfacePatch>(),
            new byte[8],
            new List<ValidationIssue>());

        var dummyAnalysis = new WorkflowAnalysisResult
        {
            Compliance = 100.0,
            MaxDisplacement = 0.5,
            IsOutdated = false
        };

        service.State.PreparedModel = dummyPrep;
        service.State.AnalysisResult = dummyAnalysis;

        Assert.False(service.State.PreparedModel.IsOutdated);
        Assert.False(service.State.AnalysisResult.IsOutdated);

        // 1. Material change: should invalidate ONLY AnalysisResult
        service.SetMaterial(MaterialDefinition.CreatePreset(MaterialPresetKind.Petg));
        Assert.False(service.State.PreparedModel.IsOutdated);
        Assert.True(service.State.AnalysisResult.IsOutdated);

        // Reset
        service.State.AnalysisResult.IsOutdated = false;

        // 2. Resolution change: should invalidate BOTH PreparedModel and AnalysisResult
        service.SetResolution(new ResolutionConfig { TargetSolidElements = 50_000 });
        Assert.True(service.State.PreparedModel.IsOutdated);
        Assert.True(service.State.AnalysisResult.IsOutdated);

        // Reset
        service.State.PreparedModel.IsOutdated = false;
        service.State.AnalysisResult.IsOutdated = false;

        // 3. Selection change (add load): should invalidate BOTH
        service.AddLoad("TestLoad", new[] { 0, 1 }, 100f);
        Assert.True(service.State.PreparedModel.IsOutdated);
        Assert.True(service.State.AnalysisResult.IsOutdated);
    }

    [Fact]
    public void Test_ProjectRoundTrip_AndHashValidation()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Mycel_TestProj_" + Guid.NewGuid().ToString("N"));
        try
        {
            var boxTris = StlFixtures.Box(new Vector3(30f, 10f, 10f));
            var mesh = IndexedMeshFromTriangles(boxTris);

            var state = new ProjectState
            {
                Mesh = mesh,
                Unit = LengthUnit.Millimeter,
                Material = MaterialDefinition.CreatePreset(MaterialPresetKind.PlaPlus),
                Resolution = new ResolutionConfig { TargetSolidElements = 80_000, Preset = ResolutionPreset.Custom }
            };

            var load = new LoadRegion
            {
                Name = "TipLoad",
                Triangles = new[] { 0, 1, 2 },
                MagnitudeNewtons = 750f,
                DirectionMode = LoadDirectionMode.AxisNegativeZ
            };
            load.UpdateGeometryMetrics(mesh);
            state.Loads.Add(load);

            var supp = new SupportRegion
            {
                Name = "BaseClamp",
                Triangles = new[] { 4, 5 },
                FixX = true,
                FixY = true,
                FixZ = false
            };
            supp.UpdateGeometryMetrics(mesh);
            state.Supports.Add(supp);

            // 1. Save project
            ProjectSerializer.Save(tempDir, state);

            Assert.True(File.Exists(Path.Combine(tempDir, "project.json")));
            Assert.True(File.Exists(Path.Combine(tempDir, "model.stl")));

            // 2. Open project
            var loadedState = ProjectSerializer.Open(tempDir);

            Assert.NotNull(loadedState.Mesh);
            Assert.Equal(mesh.TriangleCount, loadedState.Mesh.TriangleCount);
            // StlReader welds duplicate triangle vertices: an axis-aligned box has exactly 8 unique vertices
            Assert.Equal(8, loadedState.Mesh.VertexCount);
            Assert.Equal(LengthUnit.Millimeter, loadedState.Unit);

            Assert.Equal(MaterialPresetKind.PlaPlus, loadedState.Material.PresetKind);
            Assert.Equal(2700.0, loadedState.Material.E);
            Assert.Equal(80_000, loadedState.Resolution.TargetSolidElements);

            Assert.Single(loadedState.Loads);
            Assert.Equal("TipLoad", loadedState.Loads[0].Name);
            Assert.Equal(new[] { 0, 1, 2 }, loadedState.Loads[0].Triangles);
            Assert.Equal(750f, loadedState.Loads[0].MagnitudeNewtons);
            Assert.Equal(LoadDirectionMode.AxisNegativeZ, loadedState.Loads[0].DirectionMode);

            Assert.Single(loadedState.Supports);
            Assert.Equal("BaseClamp", loadedState.Supports[0].Name);
            Assert.Equal(new[] { 4, 5 }, loadedState.Supports[0].Triangles);
            Assert.False(loadedState.Supports[0].FixZ);

            // 3. Tamper with model.stl to test hash mismatch refusal
            string modelPath = Path.Combine(tempDir, "model.stl");
            byte[] fileBytes = File.ReadAllBytes(modelPath);
            fileBytes[^1] ^= 0xFF; // Flip last byte
            File.WriteAllBytes(modelPath, fileBytes);

            Assert.Throws<InvalidOperationException>(() => ProjectSerializer.Open(tempDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task Test_GoldenWorkflow_CantileverBar()
    {
        // Dimensions: L = 40 mm, H = 10 mm, W = 10 mm
        var boxTris = StlFixtures.Box(new Vector3(40f, 10f, 10f));
        // Shift so origin is at min corner: x in [0, 40], y in [0, 10], z in [0, 10]
        var shiftedTris = StlFixtures.Transform(boxTris, Matrix4x4.CreateTranslation(20f, 5f, 5f));
        var mesh = IndexedMeshFromTriangles(shiftedTris);

        var service = new WorkflowService();
        service.LoadMesh(mesh);

        // 1. Identify support triangles at x = 0 face
        var supportTris = new List<int>();
        var loadTris = new List<int>();

        var triNormals = mesh.TriangleNormals.Span;
        var indices = mesh.Indices.Span;
        var positions = mesh.Positions.Span;

        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            Vector3 v0 = positions[indices[i * 3 + 0]];
            Vector3 v1 = positions[indices[i * 3 + 1]];
            Vector3 v2 = positions[indices[i * 3 + 2]];
            Vector3 c = (v0 + v1 + v2) / 3.0f;

            if (c.X < 1.0f && Vector3.Dot(triNormals[i], -Vector3.UnitX) > 0.9f)
            {
                supportTris.Add(i);
            }
            if (c.X > 39.0f && Vector3.Dot(triNormals[i], Vector3.UnitX) > 0.9f)
            {
                loadTris.Add(i);
            }
        }

        Assert.True(supportTris.Count > 0, "Support triangles found on x=0");
        Assert.True(loadTris.Count > 0, "Load triangles found on x=40");

        service.AddSupport("Clamp", supportTris.ToArray(), fixX: true, fixY: true, fixZ: true);
        service.AddLoad("TipForce", loadTris.ToArray(), 500f, LoadDirectionMode.AxisNegativeZ);

        // Material: PLA (E = 3000 MPa, nu = 0.36)
        service.SetMaterial(MaterialDefinition.CreatePreset(MaterialPresetKind.Pla));

        // Resolution: Draft (~2.0 mm voxels -> ~20 x 5 x 5 elements = 500 elements)
        service.SetResolution(new ResolutionConfig { ExplicitH = 2.0f });

        // 2. Prepare
        var prep = await service.PrepareAsync();
        Assert.NotNull(prep);
        Assert.NotNull(prep.FeModel);
        Assert.False(prep.IsOutdated);
        Assert.DoesNotContain(prep.ValidationIssues, issue => issue.Severity == ValidationSeverity.Error);

        // 3. Static FEA
        var result = await service.RunStaticAnalysisAsync();
        Assert.NotNull(result);
        Assert.False(result.IsOutdated);

        // Sanity on physical quantities
        Assert.True(result.Compliance > 0.0, $"Compliance must be positive, was {result.Compliance}");
        Assert.True(result.MaxDisplacement > 0.0, $"Max displacement must be positive, was {result.MaxDisplacement}");
        Assert.True(result.MaxVonMises > 0.0, $"Max von Mises must be positive, was {result.MaxVonMises}");
        Assert.True(result.P99VonMises > 0.0, $"P99 von Mises must be positive, was {result.P99VonMises}");
        Assert.True(result.P99VonMises <= result.MaxVonMises, "P99 von Mises must be <= max von Mises");

        // Surface vertex scalar fields populated
        Assert.Equal(mesh.VertexCount, result.VertexVonMises.Length);
        Assert.Equal(mesh.VertexCount, result.VertexDisplacements.Length);

        // Safety factors
        Assert.True(result.SafetyFactor > 0.0, $"Safety factor must be positive, was {result.SafetyFactor}");
        Assert.True(result.SafetyFactorP99 >= result.SafetyFactor, "SF_p99 should be >= SF_max");
    }

    private static IndexedMesh IndexedMeshFromTriangles(IEnumerable<Tri> tris)
    {
        var triList = tris.ToList();
        var positions = new List<Vector3>();
        var indices = new List<int>();

        foreach (var t in triList)
        {
            int i0 = positions.Count;
            positions.Add(t.A);
            positions.Add(t.B);
            positions.Add(t.C);
            indices.Add(i0);
            indices.Add(i0 + 1);
            indices.Add(i0 + 2);
        }

        return new IndexedMesh(positions.ToArray(), indices.ToArray());
    }

    private static FeModel CreateDummyFeModel()
    {
        var grid = new GridSpec(Vector3.Zero, 1.0f, 2, 2, 2);
        byte[] domain = new byte[8];
        Array.Fill(domain, (byte)1);
        var mat = new FeMaterial(3000.0, 0.3);
        int[] fixedDofs = { 0, 1, 2, 3, 4, 5 };
        return new FeModel(grid, domain, mat, fixedDofs, Array.Empty<LoadCase>());
    }
}
