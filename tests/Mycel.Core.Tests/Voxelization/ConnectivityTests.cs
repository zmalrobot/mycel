using System.Numerics;
using Mycel.Core.Voxelization;
using Xunit;

namespace Mycel.Core.Tests.Voxelization;

public sealed class ConnectivityTests
{
    [Fact]
    public void AnalyzeAndFilter_CornerConnectedVoxels_TreatedAsSingleComponent()
    {
        // Two voxels sharing only a corner: (0,0,0) and (1,1,1)
        GridSpec grid = new(Vector3.Zero, 1.0f, 3, 3, 3);
        byte[] domain = new byte[27];
        domain[grid.ElementIndex(0, 0, 0)] = 1;
        domain[grid.ElementIndex(1, 1, 1)] = 1;

        var (components, warnings) = Connectivity.AnalyzeAndFilter(grid, domain, keepOnlyLargest: false);

        Assert.Single(components);
        Assert.Equal(2, components[0].VoxelCount);
        Assert.Empty(warnings);
        Assert.Equal(1, domain[grid.ElementIndex(0, 0, 0)]);
        Assert.Equal(1, domain[grid.ElementIndex(1, 1, 1)]);
    }

    [Fact]
    public void AnalyzeAndFilter_EdgeConnectedVoxels_TreatedAsSingleComponent()
    {
        // Two voxels sharing only an edge: (0,0,0) and (1,1,0)
        GridSpec grid = new(Vector3.Zero, 1.0f, 3, 3, 3);
        byte[] domain = new byte[27];
        domain[grid.ElementIndex(0, 0, 0)] = 1;
        domain[grid.ElementIndex(1, 1, 0)] = 1;

        var (components, warnings) = Connectivity.AnalyzeAndFilter(grid, domain, keepOnlyLargest: false);

        Assert.Single(components);
        Assert.Equal(2, components[0].VoxelCount);
        Assert.Empty(warnings);
    }

    [Fact]
    public void AnalyzeAndFilter_TrulyDisconnectedVoxels_SeparatesComponentsAndFiltersSmallest()
    {
        // Component 1 at (0,0,0) and (0,0,1) -> 2 voxels
        // Component 2 at (2,2,2) -> 1 voxel
        GridSpec grid = new(Vector3.Zero, 1.0f, 3, 3, 3);
        byte[] domain = new byte[27];
        domain[grid.ElementIndex(0, 0, 0)] = 1;
        domain[grid.ElementIndex(0, 0, 1)] = 1;
        domain[grid.ElementIndex(2, 2, 2)] = 1;

        var (components, warnings) = Connectivity.AnalyzeAndFilter(grid, domain, keepOnlyLargest: true);

        Assert.Single(components);
        Assert.Equal(2, components[0].VoxelCount);
        Assert.NotEmpty(warnings);
        // The isolated voxel must have been dropped
        Assert.Equal(0, domain[grid.ElementIndex(2, 2, 2)]);
        Assert.Equal(1, domain[grid.ElementIndex(0, 0, 0)]);
        Assert.Equal(1, domain[grid.ElementIndex(0, 0, 1)]);
    }
}
