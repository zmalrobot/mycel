using System;
using System.Collections.Generic;
using TopoOpt.Core.Fem;
using TopoOpt.Core.Voxelization;

namespace TopoOpt.Core.Workflow;

public sealed class PreparedModel
{
    public FeModel FeModel { get; init; }
    public VoxelModel VoxelModel { get; init; }
    public SurfacePatch[] LoadPatches { get; init; } = Array.Empty<SurfacePatch>();
    public SurfacePatch[] SupportPatches { get; init; } = Array.Empty<SurfacePatch>();
    public byte[] PassiveShell { get; init; } = Array.Empty<byte>();
    public List<ValidationIssue> ValidationIssues { get; init; } = new();
    public bool IsOutdated { get; set; }

    public PreparedModel(
        FeModel feModel,
        VoxelModel voxelModel,
        SurfacePatch[] loadPatches,
        SurfacePatch[] supportPatches,
        byte[] passiveShell,
        List<ValidationIssue> validationIssues)
    {
        FeModel = feModel ?? throw new ArgumentNullException(nameof(feModel));
        VoxelModel = voxelModel ?? throw new ArgumentNullException(nameof(voxelModel));
        LoadPatches = loadPatches ?? Array.Empty<SurfacePatch>();
        SupportPatches = supportPatches ?? Array.Empty<SurfacePatch>();
        PassiveShell = passiveShell ?? Array.Empty<byte>();
        ValidationIssues = validationIssues ?? new();
    }
}

