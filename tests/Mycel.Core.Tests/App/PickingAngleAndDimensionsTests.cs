using System;
using System.IO;
using System.Numerics;
using Mycel.App.ViewModels;
using Mycel.Core.Geometry;
using Mycel.Core.Tests.Fixtures;
using Mycel.Core.Workflow;
using Xunit;

namespace Mycel.Core.Tests.App;

public sealed class PickingAngleAndDimensionsTests
{
    private static (IndexedMesh Mesh, MeshReport Report) CreateBox(Vector3 size)
    {
        var boxTris = StlFixtures.Box(size);
        using var ms = new MemoryStream();
        StlFixtures.WriteBinary(ms, boxTris);
        ms.Position = 0;
        return StlReader.Read(ms);
    }

    private static (IndexedMesh Mesh, MeshReport Report) CreateCylinder(float radius, float height, int segments)
    {
        var cylTris = StlFixtures.Cylinder(radius, height, segments, holeRadius: 0f);
        using var ms = new MemoryStream();
        StlFixtures.WriteBinary(ms, cylTris);
        ms.Position = 0;
        return StlReader.Read(ms);
    }

    [Fact]
    public void ModelMetrics_BoxGeometry_FormatsDimensionsAndVolumeCorrectly()
    {
        var vm = new MainWindowViewModel();
        var (mesh, report) = CreateBox(new Vector3(10f, 20f, 30f));
        var bvh = new Bvh(mesh);
        var selector = new RegionSelector(mesh);

        vm.ApplyLoadedMesh(mesh, bvh, selector, report);

        Assert.True(vm.HasModelLoaded);
        Assert.Equal("10.0 mm", vm.DimensionXText);
        Assert.Equal("20.0 mm", vm.DimensionYText);
        Assert.Equal("30.0 mm", vm.DimensionZText);
        Assert.Equal("10.0 × 20.0 × 30.0 mm", vm.DimensionsSummaryText);
        Assert.Equal("6.0 cm³", vm.VolumeCm3Text);
    }

    [Fact]
    public void ShowDimensions_Toggle_FiresNotificationEvent()
    {
        var vm = new MainWindowViewModel();
        Assert.True(vm.ShowDimensions);

        bool? notifiedValue = null;
        vm.ShowDimensionsChanged += val => notifiedValue = val;

        vm.ShowDimensions = false;
        Assert.False(vm.ShowDimensions);
        Assert.Equal(false, notifiedValue);

        vm.ShowDimensions = true;
        Assert.True(vm.ShowDimensions);
        Assert.Equal(true, notifiedValue);
    }

    [Fact]
    public void AngleThresholdChanged_RegrowsActiveLoadRegionDynamically()
    {
        var vm = new MainWindowViewModel();
        // Cylinder with 32 segments: angle between adjacent facets around the circumference is ~360 / 32 ≈ 11.25 degrees
        var (mesh, report) = CreateCylinder(10f, 20f, 32);
        var bvh = new Bvh(mesh);
        var selector = new RegionSelector(mesh);
        vm.ApplyLoadedMesh(mesh, bvh, selector, report);

        var load = new LoadItemViewModel(new LoadRegion { Name = "Test Cylinder Load" });
        vm.Loads.Add(load);
        vm.ArmPicking(load);

        Assert.True(vm.IsPickingActive);
        Assert.Equal(load, vm.ActivePickingTarget);

        // Find a triangle on the cylindrical side (normal Z component near 0)
        int sideSeedTriangle = -1;
        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            if (MathF.Abs(mesh.TriangleNormals.Span[i].Z) < 0.1f)
            {
                sideSeedTriangle = i;
                break;
            }
        }
        Assert.True(sideSeedTriangle >= 0);

        // Simulate user clicking on side facet at initial angle 5 degrees (only coplanar quad or single segment)
        vm.AngleThresholdDegrees = 5.0f;
        vm.LastClickedSeedTriangle = sideSeedTriangle;
        vm.LastClickedMode = SelectionModifyMode.Replace;
        vm.SetBaseTrianglesBeforeLastClick([]);

        int[] initialTriangles = selector.GrowRegion(sideSeedTriangle, 5.0f);
        vm.ModifyActiveSelectionTriangles(initialTriangles, SelectionModifyMode.Replace);
        int initialCount = load.Model.Triangles.Length;
        Assert.True(initialCount > 0);

        // Now simulate user dragging slider to 45 degrees:
        // Because 45 > 11.25 degrees, flood fill should wrap around adjacent curved facets and select more triangles!
        vm.AngleThresholdDegrees = 45.0f;

        int newCount = load.Model.Triangles.Length;
        Assert.True(newCount > initialCount, $"Expected new count ({newCount}) to be greater than initial ({initialCount})");
    }

    [Fact]
    public void AngleThresholdChanged_WithShiftAdd_PreservesBaseTriangles()
    {
        var vm = new MainWindowViewModel();
        var (mesh, report) = CreateCylinder(10f, 20f, 32);
        var bvh = new Bvh(mesh);
        var selector = new RegionSelector(mesh);
        vm.ApplyLoadedMesh(mesh, bvh, selector, report);

        var load = new LoadItemViewModel(new LoadRegion { Name = "Multi-patch Load" });
        vm.Loads.Add(load);
        vm.ArmPicking(load);

        // Preset base selection: triangle 0, 1
        int[] baseTriangles = [0, 1];
        load.UpdateTriangles(baseTriangles);

        // Set initial angle to 5 degrees
        vm.AngleThresholdDegrees = 5.0f;

        // Simulate Shift-clicking on a side facet
        int sideSeed = -1;
        for (int i = 2; i < mesh.TriangleCount; i++)
        {
            if (MathF.Abs(mesh.TriangleNormals.Span[i].Z) < 0.1f)
            {
                sideSeed = i;
                break;
            }
        }
        Assert.True(sideSeed >= 0);

        vm.LastClickedSeedTriangle = sideSeed;
        vm.LastClickedMode = SelectionModifyMode.Add;
        vm.SetBaseTrianglesBeforeLastClick(baseTriangles);

        // Slider moved from 5 to 30 degrees
        vm.AngleThresholdDegrees = 30.0f;

        // Base triangles must be preserved in the union
        Assert.Contains(0, load.Model.Triangles);
        Assert.Contains(1, load.Model.Triangles);
        Assert.Contains(sideSeed, load.Model.Triangles);
    }

    [Fact]
    public void DisarmPicking_ResetsLastClickedSeedTriangleAndMode()
    {
        var vm = new MainWindowViewModel();
        var load = new LoadItemViewModel(new LoadRegion { Name = "Load" });
        vm.Loads.Add(load);
        vm.ArmPicking(load);

        vm.LastClickedSeedTriangle = 17;
        vm.LastClickedMode = SelectionModifyMode.Add;
        vm.SetBaseTrianglesBeforeLastClick([1, 2, 3]);

        vm.DisarmPicking();

        Assert.False(vm.IsPickingActive);
        Assert.Null(vm.ActivePickingTarget);
        Assert.Equal(-1, vm.LastClickedSeedTriangle);
        Assert.Equal(SelectionModifyMode.Replace, vm.LastClickedMode);
    }
}

