using System;
using System.Collections.Generic;

namespace Mycel.Core.Voxelization;

public sealed record ComponentInfo(int Id, int VoxelCount, double Volume);

/// <summary>
/// Represents the voxelised domain over a regular Hex8 structured grid.
/// </summary>
public sealed class VoxelModel
{
    public GridSpec Grid { get; }

    /// <summary>
    /// Binary domain array: 1 = solid, 0 = void. Length equals Grid.TotalElements.
    /// </summary>
    public byte[] Domain { get; }

    /// <summary>
    /// Total number of solid voxels.
    /// </summary>
    public int SolidCount { get; }

    /// <summary>
    /// Total physical volume of solid voxels in mm³.
    /// </summary>
    public double SolidVolume { get; }

    /// <summary>
    /// 6-connected components found in the domain.
    /// </summary>
    public IReadOnlyList<ComponentInfo> Components { get; }

    /// <summary>
    /// Warnings produced during voxelisation (e.g. non-watertight fallback, dropped disconnected components).
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>
    /// Contiguous sorted indices of active (solid) elements.
    /// </summary>
    public int[] ActiveElements { get; }

    public VoxelModel(
        GridSpec grid,
        byte[] domain,
        int solidCount,
        double solidVolume,
        IReadOnlyList<ComponentInfo> components,
        IReadOnlyList<string> warnings,
        int[] activeElements)
    {
        Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        Domain = domain ?? throw new ArgumentNullException(nameof(domain));
        SolidCount = solidCount;
        SolidVolume = solidVolume;
        Components = components ?? Array.Empty<ComponentInfo>();
        Warnings = warnings ?? Array.Empty<string>();
        ActiveElements = activeElements ?? Array.Empty<int>();
    }
}

