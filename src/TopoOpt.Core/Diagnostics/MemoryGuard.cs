using System;
using System.Globalization;
using System.IO;

namespace TopoOpt.Core.Diagnostics;

public enum MemoryGuardLevel
{
    Ok,
    Warning,
    Refused
}

public readonly record struct MemoryAssessment(
    MemoryGuardLevel Level,
    long EstimatedBytes,
    long AvailableBytes,
    double UsageRatio,
    string Message);

/// <summary>
/// Proactive memory safety guard that estimates heap and scratch memory requirements
/// before voxelisation and optimization runs, preventing out-of-memory crashes and thrashing.
/// Warns above 70% of available RAM, refuses above 90%.
/// </summary>
public static class MemoryGuard
{
    public const double WarningThreshold = 0.70;
    public const double RefusalThreshold = 0.90;

    /// <summary>
    /// Estimates memory required for voxelisation, boundary mesh analysis, and patch generation.
    /// </summary>
    public static long EstimatePrepareBytes(long boundingBoxVoxels, int triangleCount)
    {
        // 1 byte/voxel occupancy bitset/grid + 4 bytes/voxel index mapping for active solid cells
        long voxelGridBytes = boundingBoxVoxels * 5L;

        // Voxeliser 2D binning & ray-triangle CSR structure (~48 bytes per triangle)
        long triangleBinnedBytes = triangleCount * 48L;

        // Surface patch detection, boundary traversal, dilation scratch buffers (~64 MB base)
        long scratchBytes = 64L * 1024L * 1024L;

        return voxelGridBytes + triangleBinnedBytes + scratchBytes;
    }

    /// <summary>
    /// Estimates memory required for Hex8 FEA, SIMP optimization loop, PCG scratch vectors,
    /// density filtering, and live iso-surface extraction buffers.
    /// </summary>
    public static long EstimateRunBytes(long solidElements, long nodes, int maxIterations)
    {
        // Hex8 PCG solver vectors: U, F, R, Z, P, AP (6 vectors * 3 DOFs * 8 bytes double = 144 bytes / node)
        // Plus Jacobi inverse diagonal preconditioner (24 bytes / node) -> ~168 bytes / node
        long solverBytes = nodes * 168L;

        // Density & sensitivity arrays: rho, rhoPhys, dc, dv, rhoOld, rhoTilde (6 * 8 bytes = 48 bytes / solid element)
        long densityBytes = solidElements * 48L;

        // Sensitivity filter neighborhood CSR: ~33 neighbors for rmin=1.5h (33 * 8 bytes = 264 bytes / solid element)
        long filterBytes = solidElements * 264L;

        // Iteration snapshot buffer (Channel capacity 8 + scratch) ~ 8 * solidElements * 4 bytes float
        long snapshotBytes = Math.Min(8L, (long)maxIterations) * solidElements * 4L;

        // Iso-surface extraction buffers (Surface Nets dual grid, vertex buffer, index buffer): ~128 MB base
        long extractionScratchBytes = 128L * 1024L * 1024L;

        return solverBytes + densityBytes + filterBytes + snapshotBytes + extractionScratchBytes;
    }

    /// <summary>
    /// Evaluates estimated bytes against available system RAM.
    /// </summary>
    public static MemoryAssessment Assess(long estimatedBytes)
    {
        long availableBytes = GetAvailablePhysicalMemoryBytes();
        if (availableBytes <= 0)
        {
            availableBytes = 4L * 1024L * 1024L * 1024L; // Default fallback to 4 GB if cannot detect
        }

        double ratio = (double)estimatedBytes / availableBytes;
        double estMb = estimatedBytes / (1024.0 * 1024.0);
        double estGb = estMb / 1024.0;
        double availGb = availableBytes / (1024.0 * 1024.0 * 1024.0);

        if (ratio > RefusalThreshold)
        {
            string msg = string.Format(
                CultureInfo.InvariantCulture,
                "Estimated memory ({0:F1} GB) exceeds {1:P0} of available RAM ({2:F1} GB). Operation refused to prevent system freeze or out-of-memory crash. Please increase element size (coarser resolution).",
                estGb, RefusalThreshold, availGb);
            return new MemoryAssessment(MemoryGuardLevel.Refused, estimatedBytes, availableBytes, ratio, msg);
        }

        if (ratio > WarningThreshold)
        {
            string msg = string.Format(
                CultureInfo.InvariantCulture,
                "Estimated memory ({0:F1} GB) exceeds {1:P0} of available RAM ({2:F1} GB). Optimization may cause memory pressure or swap file usage.",
                estGb, WarningThreshold, availGb);
            return new MemoryAssessment(MemoryGuardLevel.Warning, estimatedBytes, availableBytes, ratio, msg);
        }

        return new MemoryAssessment(
            MemoryGuardLevel.Ok,
            estimatedBytes,
            availableBytes,
            ratio,
            string.Format(CultureInfo.InvariantCulture, "Estimated memory: {0:F1} MB ({1:P1} of available {2:F1} GB RAM).", estMb, ratio, availGb));
    }

    /// <summary>
    /// Gets the available physical memory in bytes, checking /proc/meminfo on Linux with GC fallback.
    /// </summary>
    public static long GetAvailablePhysicalMemoryBytes()
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                if (File.Exists("/proc/meminfo"))
                {
                    using StreamReader reader = new("/proc/meminfo");
                    string? line;
                    while ((line = reader.ReadLine()) is not null)
                    {
                        if (line.StartsWith("MemAvailable:", StringComparison.OrdinalIgnoreCase))
                        {
                            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long kb))
                            {
                                return kb * 1024L;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fall back to GC memory info
            }
        }

        GCMemoryInfo mem = GC.GetGCMemoryInfo();
        return mem.TotalAvailableMemoryBytes;
    }
}

