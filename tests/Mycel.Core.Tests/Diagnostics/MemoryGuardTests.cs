using System;
using Mycel.Core.Diagnostics;
using Xunit;

namespace Mycel.Core.Tests.Diagnostics;

public sealed class MemoryGuardTests
{
    [Fact]
    public void EstimatePrepareBytes_ReasonableGrid_ReturnsPositiveSize()
    {
        // 100x100x100 = 1M voxels, 20k triangles
        long bytes = MemoryGuard.EstimatePrepareBytes(1_000_000, 20_000);
        Assert.True(bytes > 64 * 1024 * 1024); // Greater than base scratch
        Assert.True(bytes < 500 * 1024 * 1024); // Less than 500 MB
    }

    [Fact]
    public void EstimateRunBytes_100kElements_ReturnsExpectedMagnitude()
    {
        // 100k elements, ~120k nodes, 50 iterations
        long bytes = MemoryGuard.EstimateRunBytes(100_000, 120_000, 50);
        double mb = bytes / (1024.0 * 1024.0);
        // Expect ~128 MB base + ~20 MB solver + ~30 MB filter/densities ~ 170-200 MB
        Assert.True(mb >= 150.0 && mb <= 300.0, $"Estimated {mb} MB is outside expected range 150-300 MB.");
    }

    [Fact]
    public void Assess_SmallEstimate_ReturnsOk()
    {
        long smallBytes = 50 * 1024 * 1024; // 50 MB
        var assessment = MemoryGuard.Assess(smallBytes);
        Assert.Equal(MemoryGuardLevel.Ok, assessment.Level);
        Assert.True(assessment.UsageRatio < 0.70);
    }

    [Fact]
    public void Assess_ExcessiveEstimate_ReturnsRefused()
    {
        long hugeBytes = 500L * 1024L * 1024L * 1024L; // 500 GB
        var assessment = MemoryGuard.Assess(hugeBytes);
        Assert.Equal(MemoryGuardLevel.Refused, assessment.Level);
        Assert.True(assessment.UsageRatio > 0.90);
        Assert.Contains("refused", assessment.Message, StringComparison.OrdinalIgnoreCase);
    }
}

