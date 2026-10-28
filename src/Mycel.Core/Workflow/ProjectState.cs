using System;
using System.Collections.Generic;
using Mycel.Core.Geometry;

namespace Mycel.Core.Workflow;

public sealed class ProjectState
{
    public IndexedMesh? Mesh { get; set; }
    public MeshReport? MeshReport { get; set; }
    public string? FilePath { get; set; }
    public LengthUnit Unit { get; set; } = LengthUnit.Millimeter;
    public string? StlSha256 { get; set; }

    public List<LoadRegion> Loads { get; } = new();
    public List<SupportRegion> Supports { get; } = new();
    public List<PreserveRegion> PreserveRegions { get; } = new();

    public MaterialDefinition Material { get; set; } = MaterialDefinition.CreatePreset(MaterialPresetKind.Pla);
    public ResolutionConfig Resolution { get; set; } = new();

    public PreparedModel? PreparedModel { get; set; }
    public WorkflowAnalysisResult? AnalysisResult { get; set; }

    public long StateRevision { get; private set; }

    public event Action? StateChanged;
    public event Action? PreparationInvalidated;
    public event Action? AnalysisInvalidated;

    public void NotifySelectionChanged()
    {
        StateRevision++;
        if (PreparedModel != null)
        {
            PreparedModel.IsOutdated = true;
        }
        if (AnalysisResult != null)
        {
            AnalysisResult.IsOutdated = true;
        }

        PreparationInvalidated?.Invoke();
        AnalysisInvalidated?.Invoke();
        StateChanged?.Invoke();
    }

    public void NotifyResolutionChanged()
    {
        StateRevision++;
        if (PreparedModel != null)
        {
            PreparedModel.IsOutdated = true;
        }
        if (AnalysisResult != null)
        {
            AnalysisResult.IsOutdated = true;
        }

        PreparationInvalidated?.Invoke();
        AnalysisInvalidated?.Invoke();
        StateChanged?.Invoke();
    }

    public void NotifyMaterialChanged()
    {
        StateRevision++;
        if (AnalysisResult != null)
        {
            AnalysisResult.IsOutdated = true;
        }

        AnalysisInvalidated?.Invoke();
        StateChanged?.Invoke();
    }

    public void Clear()
    {
        Mesh = null;
        MeshReport = null;
        FilePath = null;
        StlSha256 = null;
        Loads.Clear();
        Supports.Clear();
        PreserveRegions.Clear();
        PreparedModel = null;
        AnalysisResult = null;
        StateRevision++;
        StateChanged?.Invoke();
    }
}

