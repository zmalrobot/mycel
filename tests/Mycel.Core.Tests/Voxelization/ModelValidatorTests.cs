using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Mycel.Core.Geometry;
using Mycel.Core.Tests.Fixtures;
using Mycel.Core.Voxelization;
using Xunit;

namespace Mycel.Core.Tests.Voxelization;

public sealed class ModelValidatorTests
{
    [Fact]
    public void Validate_NoLoadsAndNoSupports_EmitsErrors()
    {
        IndexedMesh box = CreateMesh(StlFixtures.Box(new Vector3(10f, 10f, 10f)));
        GridSpec grid = GridSpec.FromVoxelSize(box, 1.0f);
        var (model, _) = Voxeliser.Voxelise(box, grid);

        var issues = ModelValidator.Validate(model, box, []);

        Assert.Contains(issues, i => i.Code == ModelValidator.ErrNoLoads && i.Severity == ValidationSeverity.Error);
        Assert.Contains(issues, i => i.Code == ModelValidator.ErrNoSupports && i.Severity == ValidationSeverity.Error);
        Assert.Contains(issues, i => i.Code == ModelValidator.InfoResourceEstimate && i.Severity == ValidationSeverity.Info);
    }

    [Fact]
    public void Validate_EmptyPatch_EmitsErrEmptyPatch()
    {
        IndexedMesh box = CreateMesh(StlFixtures.Box(new Vector3(10f, 10f, 10f)));
        GridSpec grid = GridSpec.FromVoxelSize(box, 1.0f);
        var (model, _) = Voxeliser.Voxelise(box, grid);

        Guid emptyId = Guid.NewGuid();
        SurfaceSelection emptySel = new()
        {
            Id = emptyId,
            Kind = SelectionKind.Load,
            Name = "Empty Load",
            TriangleIndices = []
        };

        var issues = ModelValidator.Validate(model, box, [emptySel]);

        Assert.Contains(issues, i => i.Code == ModelValidator.ErrEmptyPatch && i.RelatedSelectionId == emptyId);
    }

    [Fact]
    public void Validate_PointLoadPatchWithLessThanFourNodes_EmitsErrPointLoad()
    {
        IndexedMesh box = CreateMesh(StlFixtures.Box(new Vector3(10f, 10f, 10f)));
        GridSpec grid = GridSpec.FromVoxelSize(box, 1.0f);
        var (model, _) = Voxeliser.Voxelise(box, grid);

        // Fabricate a patch that only touches 3 nodes
        Guid id = Guid.NewGuid();
        SurfacePatch mockPatch = new(
            Faces: [],
            Nodes: [0, 1, 2],
            NodeArea: [0.25, 0.25, 0.25],
            BBox: (Vector3.Zero, Vector3.One),
            TouchesLargestComponent: true);

        var issues = ModelValidator.ValidatePatches(model, box, [
            (id, SelectionKind.Load, mockPatch),
            (Guid.NewGuid(), SelectionKind.Support, CreateValidPatch(model, 10))
        ]);

        Assert.Contains(issues, i => i.Code == ModelValidator.ErrPointLoad && i.RelatedSelectionId == id);
    }

    [Fact]
    public void Validate_LoadAndSupportSharingNodes_EmitsErrLoadSupportOverlap()
    {
        IndexedMesh box = CreateMesh(StlFixtures.Box(new Vector3(10f, 10f, 10f)));
        GridSpec grid = GridSpec.FromVoxelSize(box, 1.0f);
        var (model, _) = Voxeliser.Voxelise(box, grid);

        Guid loadId = Guid.NewGuid();
        Guid supportId = Guid.NewGuid();

        SurfacePatch sharedPatch1 = new(
            Faces: [new VoxelFace(0, FaceDirection.PosZ, Vector3.Zero, Vector3.UnitZ)],
            Nodes: [10, 11, 20, 21],
            NodeArea: [0.25, 0.25, 0.25, 0.25],
            BBox: (Vector3.Zero, Vector3.One),
            TouchesLargestComponent: true);

        SurfacePatch sharedPatch2 = new(
            Faces: [new VoxelFace(1, FaceDirection.PosZ, Vector3.UnitX, Vector3.UnitZ)],
            Nodes: [21, 22, 31, 32], // Node 21 is shared!
            NodeArea: [0.25, 0.25, 0.25, 0.25],
            BBox: (Vector3.Zero, Vector3.One),
            TouchesLargestComponent: true);

        var issues = ModelValidator.ValidatePatches(model, box, [
            (loadId, SelectionKind.Load, sharedPatch1),
            (supportId, SelectionKind.Support, sharedPatch2)
        ]);

        Assert.Contains(issues, i => i.Code == ModelValidator.ErrLoadSupportOverlap);
    }

    [Fact]
    public void Validate_UnderResolution_EmitsWarnUnderResolution()
    {
        // Sphere with very coarse resolution so discretized volume deviates > 5%
        float r = 10f;
        IndexedMesh sphere = CreateMesh(StlFixtures.Sphere(r, 32, 32));
        GridSpec grid = GridSpec.FromVoxelSize(sphere, 4.0f, padding: 0);
        var (model, _) = Voxeliser.Voxelise(sphere, grid);

        var issues = ModelValidator.Validate(model, sphere, []);

        double dev = Math.Abs(model.SolidVolume - sphere.SignedVolume) / sphere.SignedVolume;
        if (dev > 0.05)
        {
            Assert.Contains(issues, i => i.Code == ModelValidator.WarnUnderResolution);
        }
    }

    private static SurfacePatch CreateValidPatch(VoxelModel model, int baseNode)
    {
        return new SurfacePatch(
            Faces: [new VoxelFace(0, FaceDirection.PosZ, Vector3.Zero, Vector3.UnitZ)],
            Nodes: [baseNode, baseNode + 1, baseNode + 2, baseNode + 3],
            NodeArea: [0.25, 0.25, 0.25, 0.25],
            BBox: (Vector3.Zero, Vector3.One),
            TouchesLargestComponent: true);
    }

    private static IndexedMesh CreateMesh(List<Tri> tris)
    {
        using MemoryStream ms = new();
        StlFixtures.WriteBinary(ms, tris);
        ms.Position = 0;
        return StlReader.Read(ms).Mesh;
    }
}

