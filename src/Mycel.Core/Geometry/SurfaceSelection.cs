using System;

namespace Mycel.Core.Geometry;

/// <summary>
/// Functional role of a user-selected surface region.
/// </summary>
public enum SelectionKind
{
    Load,
    Support,
    Preserve
}

/// <summary>
/// Represents a named group of selected surface triangles on an <see cref="IndexedMesh"/>.
/// </summary>
public sealed class SurfaceSelection
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public SelectionKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public int[] TriangleIndices { get; set; } = [];
    public uint ColorRgba { get; set; }

    public static uint DefaultColorForKind(SelectionKind kind) => kind switch
    {
        SelectionKind.Load => 0xFF5252FFu,     // Coral Red (RGBA)
        SelectionKind.Support => 0x3B82F6FFu,  // Royal Blue (RGBA)
        SelectionKind.Preserve => 0x10B981FFu, // Emerald Green (RGBA)
        _ => 0xF59E0BFFu
    };
}

