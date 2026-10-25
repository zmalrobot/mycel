using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using TopoOpt.Core.Geometry;

namespace TopoOpt.Core.Voxelization;

public static class ModelValidator
{
    public const string ErrEmptyPatch = "ERR_EMPTY_PATCH";
    public const string ErrPointLoad = "ERR_POINT_LOAD";
    public const string ErrLoadSupportOverlap = "ERR_LOAD_SUPPORT_OVERLAP";
    public const string ErrNoSupports = "ERR_NO_SUPPORTS";
    public const string ErrNoLoads = "ERR_NO_LOADS";
    public const string ErrDisconnectedLoad = "ERR_DISCONNECTED_LOAD";
    public const string WarnUnderResolution = "WARN_UNDER_RESOLUTION";
    public const string WarnVolumeDeviation = "WARN_VOLUME_DEVIATION";
    public const string InfoResourceEstimate = "INFO_RESOURCE_ESTIMATE";

    public static List<ValidationIssue> Validate(
        VoxelModel model,
        IndexedMesh mesh,
        IReadOnlyList<SurfaceSelection> selections)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(mesh);

        selections ??= Array.Empty<SurfaceSelection>();
        var patches = new List<(Guid Id, SelectionKind Kind, SurfacePatch Patch)>(selections.Count);

        foreach (var sel in selections)
        {
            var patch = SurfacePatchBuilder.Build(model, mesh, sel.TriangleIndices);
            patches.Add((sel.Id, sel.Kind, patch));
        }

        return ValidatePatches(model, mesh, patches);
    }

    public static List<ValidationIssue> ValidatePatches(
        VoxelModel model,
        IndexedMesh mesh,
        IReadOnlyList<(Guid Id, SelectionKind Kind, SurfacePatch Patch)> patches)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(mesh);

        List<ValidationIssue> issues = new();
        patches ??= Array.Empty<(Guid, SelectionKind, SurfacePatch)>();

        bool hasSupport = false;
        bool hasLoad = false;

        HashSet<int> supportNodes = new();

        foreach (var (id, kind, patch) in patches)
        {
            if (kind == SelectionKind.Support)
            {
                hasSupport = true;
                foreach (int n in patch.Nodes)
                {
                    supportNodes.Add(n);
                }
            }
            else if (kind == SelectionKind.Load)
            {
                hasLoad = true;
            }

            if (patch.Nodes.Length == 0)
            {
                issues.Add(new ValidationIssue(
                    ErrEmptyPatch,
                    ValidationSeverity.Error,
                    $"Selection ({kind}) mapped to 0 voxel nodes. The region does not intersect the active voxel boundary.",
                    RelatedSelectionId: id));
            }
            else
            {
                if (kind == SelectionKind.Load && patch.Nodes.Length < 4)
                {
                    issues.Add(new ValidationIssue(
                        ErrPointLoad,
                        ValidationSeverity.Error,
                        $"Load selection mapped to {patch.Nodes.Length} nodes (< 4). Numerical point loads cause stress singularities. Enlarge the selection or refine the voxel resolution.",
                        RelatedSelectionId: id));
                }

                if (!patch.TouchesLargestComponent)
                {
                    issues.Add(new ValidationIssue(
                        ErrDisconnectedLoad,
                        ValidationSeverity.Error,
                        $"Selection ({kind}) is on a disconnected component from the main structural solid domain.",
                        RelatedSelectionId: id));
                }
            }
        }

        if (!hasSupport)
        {
            issues.Add(new ValidationIssue(
                ErrNoSupports,
                ValidationSeverity.Error,
                "No support (boundary constraint) selection defined. The model is unconstrained and free to undergo rigid-body motion."));
        }

        if (!hasLoad)
        {
            issues.Add(new ValidationIssue(
                ErrNoLoads,
                ValidationSeverity.Error,
                "No applied load selection defined. The model has zero external forces."));
        }

        // Check overlap between load and support nodes
        foreach (var (id, kind, patch) in patches)
        {
            if (kind == SelectionKind.Load)
            {
                int overlapCount = patch.Nodes.Count(supportNodes.Contains);
                if (overlapCount > 0)
                {
                    issues.Add(new ValidationIssue(
                        ErrLoadSupportOverlap,
                        ValidationSeverity.Error,
                        $"Load selection shares {overlapCount} node(s) with fixed support regions. Boundary constraints override applied loads.",
                        RelatedSelectionId: id));
                }
            }
        }

        // Geometric resolution check
        Vector3 span = mesh.BBox.Max - mesh.BBox.Min;
        float minSpan = Math.Min(span.X, Math.Min(span.Y, span.Z));
        double voxelsAcross = minSpan / model.Grid.H;
        if (voxelsAcross < 3.0)
        {
            issues.Add(new ValidationIssue(
                WarnUnderResolution,
                ValidationSeverity.Warning,
                $"Mesh thinnest bounding dimension ({minSpan:F2} mm) has only {voxelsAcross:F1} voxels across (< 3). Structural bending and shear will be poorly captured."));
        }

        // Volume deviation check
        if (mesh.SignedVolume > 0)
        {
            double volumeDeviation = Math.Abs(model.SolidVolume - mesh.SignedVolume) / mesh.SignedVolume;
            if (volumeDeviation > 0.05)
            {
                issues.Add(new ValidationIssue(
                    WarnUnderResolution,
                    ValidationSeverity.Warning,
                    $"Discretized voxel volume ({model.SolidVolume:N1} mm³) deviates by {volumeDeviation * 100:F1}% (> 5%) from mesh volume ({mesh.SignedVolume:N1} mm³). Check resolution for thin features."));
            }
            else if (volumeDeviation > 0.10)
            {
                issues.Add(new ValidationIssue(
                    WarnVolumeDeviation,
                    ValidationSeverity.Warning,
                    $"Voxelised solid volume ({model.SolidVolume:N1} mm³) deviates by {volumeDeviation * 100:F1}% from mesh volume ({mesh.SignedVolume:N1} mm³)."));
            }
        }

        // Resource estimation
        long totalElements = model.Grid.TotalElements;
        int activeElements = model.SolidCount;
        long totalNodes = model.Grid.TotalNodes;
        long totalDofs = totalNodes * 3;

        HashSet<int> activeNodeSet = new();
        var grid = model.Grid;
        for (int i = 0; i < model.ActiveElements.Length; i++)
        {
            int elem = model.ActiveElements[i];
            grid.InverseElementIndex(elem, out int ix, out int iy, out int iz);

            activeNodeSet.Add((int)grid.NodeIndex(ix, iy, iz));
            activeNodeSet.Add((int)grid.NodeIndex(ix + 1, iy, iz));
            activeNodeSet.Add((int)grid.NodeIndex(ix, iy + 1, iz));
            activeNodeSet.Add((int)grid.NodeIndex(ix + 1, iy + 1, iz));
            activeNodeSet.Add((int)grid.NodeIndex(ix, iy, iz + 1));
            activeNodeSet.Add((int)grid.NodeIndex(ix + 1, iy, iz + 1));
            activeNodeSet.Add((int)grid.NodeIndex(ix, iy + 1, iz + 1));
            activeNodeSet.Add((int)grid.NodeIndex(ix + 1, iy + 1, iz + 1));
        }

        int activeNodes = activeNodeSet.Count;
        long activeDofs = (long)activeNodes * 3;

        double estBytes = 6.0 * activeDofs * sizeof(double) + activeElements * 16.0;
        double estMb = estBytes / (1024.0 * 1024.0);

        issues.Add(new ValidationIssue(
            InfoResourceEstimate,
            ValidationSeverity.Info,
            $"Grid: {activeElements:N0} active elements ({totalElements:N0} total), {activeDofs:N0} active DOFs ({totalDofs:N0} total). Estimated PCG solver memory: ~{estMb:F1} MB."));

        return issues;
    }
}
