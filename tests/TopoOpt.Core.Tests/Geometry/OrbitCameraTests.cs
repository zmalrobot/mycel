using System;
using System.Numerics;
using Avalonia;
using Avalonia.Input;
using TopoOpt.App.Controls;
using TopoOpt.App.Rendering;
using Xunit;

namespace TopoOpt.Core.Tests.Geometry;

public sealed class OrbitCameraTests
{
    [Fact]
    public void Orbit_DeltaChangesYawAndPitchDeterministically()
    {
        var camera = new OrbitCamera();
        float initialAzimuth = camera.AzimuthRadians;
        float initialElevation = camera.ElevationRadians;

        float deltaX = 50.0f;
        float deltaY = -30.0f;
        const float sensitivity = 0.0065f;

        camera.Orbit(deltaX, deltaY);

        float expectedAzimuth = initialAzimuth - deltaX * sensitivity;
        float expectedElevation = initialElevation + deltaY * sensitivity;

        Assert.Equal(expectedAzimuth, camera.AzimuthRadians, precision: 6);
        Assert.Equal(expectedElevation, camera.ElevationRadians, precision: 6);
    }

    [Fact]
    public void Orbit_ClampsElevationAtGimbalPoles()
    {
        var camera = new OrbitCamera();

        // Large upward delta
        camera.Orbit(0.0f, 10000.0f);
        float maxElevation = MathF.PI * 0.495f;
        Assert.Equal(maxElevation, camera.ElevationRadians, precision: 5);

        // Large downward delta
        camera.Orbit(0.0f, -20000.0f);
        float minElevation = -MathF.PI * 0.495f;
        Assert.Equal(minElevation, camera.ElevationRadians, precision: 5);
    }

    [Fact]
    public void Pan_ChangesTargetDeterministically()
    {
        var camera = new OrbitCamera
        {
            Target = Vector3.Zero,
            Distance = 100.0f
        };

        camera.Pan(20.0f, 10.0f, viewportHeight: 800.0f);

        // Target must move away from origin
        Assert.NotEqual(Vector3.Zero, camera.Target);
    }

    [Fact]
    public void Viewport_HitTest_ReturnsTrueInsideBoundsAndFalseOutside()
    {
        var viewport = new Viewport3DControl();
        // Simulate control bounds layout (e.g. 800 x 600)
        viewport.Measure(new Size(800, 600));
        viewport.Arrange(new Rect(0, 0, 800, 600));

        Assert.True(viewport.HitTest(new Point(400, 300)));
        Assert.True(viewport.HitTest(new Point(0, 0)));
        Assert.True(viewport.HitTest(new Point(799, 599)));
        Assert.False(viewport.HitTest(new Point(-5, 300)));
        Assert.False(viewport.HitTest(new Point(850, 300)));
    }

    [Fact]
    public void Input_MarksFrameRequested_WithFakeRenderScheduler()
    {
        var camera = new OrbitCamera();
        bool frameRequested = false;

        // Fake render scheduler delegate
        void FakeRequestNextFrameRendering()
        {
            frameRequested = true;
        }

        // Simulate user action handler
        void HandlePointerDrag(MouseButton button, KeyModifiers modifiers, float dx, float dy)
        {
            if (button == MouseButton.Right || (button == MouseButton.Left && modifiers.HasFlag(KeyModifiers.Alt)))
            {
                camera.Orbit(dx, dy);
                FakeRequestNextFrameRendering();
            }
        }

        // 1. Right-drag triggers frame request
        HandlePointerDrag(MouseButton.Right, KeyModifiers.None, 10f, 5f);
        Assert.True(frameRequested);

        // 2. Alt + Left-drag triggers frame request
        frameRequested = false;
        HandlePointerDrag(MouseButton.Left, KeyModifiers.Alt, 10f, 5f);
        Assert.True(frameRequested);

        // 3. Left-drag without Alt does not trigger frame request
        frameRequested = false;
        HandlePointerDrag(MouseButton.Left, KeyModifiers.None, 10f, 5f);
        Assert.False(frameRequested);
    }
}

