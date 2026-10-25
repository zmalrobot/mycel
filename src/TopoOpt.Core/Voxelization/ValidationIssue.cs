using System;

namespace TopoOpt.Core.Voxelization;

public enum ValidationSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Represents a validation diagnostic (error, warning, or info) for model setup prior to FEM analysis.
/// </summary>
public sealed record ValidationIssue(
    string Code,
    ValidationSeverity Severity,
    string Message,
    Guid? RelatedSelectionId = null)
{
    public string SeverityColor => Severity switch
    {
        ValidationSeverity.Error => "#EF4444",
        ValidationSeverity.Warning => "#F59E0B",
        _ => "#3B82F6"
    };
}

