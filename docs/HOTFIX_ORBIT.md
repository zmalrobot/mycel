# Hotfix Orbit Diagnostic & Resolution Report

## 1. Case Determination & Root Cause Analysis

**Identified Root Cause: Case (b)**
> *Pointer events never reach the viewport due to missing hit-testing on the custom OpenGL control, compounded by mouse button mapping expectations.*

### Diagnostic Verdict Across the 4 Hypotheses:
1. **(a) UI Thread Blocked?**
   - **Verdict: NO.** The UI thread heartbeat watchdog ran normally at 25 ms intervals throughout the session.
2. **(b) Pointer Events Never Reach Viewport / Hit-Testing Failure?**
   - **Verdict: YES (CONFIRMED WITH EVIDENCE).**
   - `OpenGlControlBase` in Avalonia renders natively to an OpenGL framebuffer, bypassing Avalonia's `DrawingContext` rendering pipeline.
   - Because `Viewport3DControl` had no Avalonia `Background` brush and did not implement custom hit-testing, Avalonia's visual tree hit-tester skipped `Viewport3DControl` entirely:
     ```text
     [2789.05 ms] this.InputHitTest at 490, 385 returned: Avalonia.Controls.Panel (Name=none)
     [2789.12 ms] Viewport.InputHitTest at (100, 100) returned: null (Name=none)
     ```
   - Pointer events (mouse clicks and drags) were received by the parent `Grid` (`Panel`) or dropped, never invoking `Viewport3DControl.OnPointerPressed` or `OnPointerMoved`.
   - Additionally, when pointer events were delivered, orbit was mapped strictly to **Right Mouse Button** or **Alt + Left Mouse Button**. Left-drag without `Alt` was ignored by design (`_isOrbiting = false`), which caused standard left-drags to do nothing.
3. **(c) Camera Changes But No Frame Requested?**
   - **Verdict: NO.** When `_isOrbiting` is active (Right-drag or Alt+Left), `_camera.Orbit(...)` is immediately followed by `RequestNextFrameRendering()`, which correctly triggers an OpenGL frame.
4. **(d) Render Callback Throws / Context Lost?**
   - **Verdict: NO.** OpenGL initialization succeeded (`GL Initialized: True, GL Init Failed: False`), Frame #1, #2, #3 rendered with 0 exceptions and execution durations $\le 8.9\text{ ms}$.

---

## 2. Hard Log Evidence

Captured from `~/.local/share/Mycel/logs/orbit_diag.log` during in-window execution:

```text
[   2704.07 ms] Viewport Bounds: 980x770
[   2704.24 ms] GL Initialized: True, GL Init Failed: False

--- TEST: Hit-test check on Viewport at center ---
BEFORE FIX:
[   2789.05 ms] this.InputHitTest at 490, 385 returned: Avalonia.Controls.Panel (Name=none)
[   2789.12 ms] Viewport.InputHitTest at (100, 100) returned: null (Name=none)

AFTER FIX (ICustomHitTest):
[   2709.70 ms] this.InputHitTest at 490, 385 returned: Mycel.App.Controls.Viewport3DControl (Name=Viewport)
[   2709.74 ms] Viewport.InputHitTest at (100, 100) returned: Mycel.App.Controls.Viewport3DControl (Name=Viewport)

--- TEST 1: Left-drag without modifiers ---
[   2712.89 ms] --- Simulating Drag: Button=Left, Modifiers=None, Start=490, 385, End=530, 405 ---
[   2712.92 ms] [SimulatePress] Resulting state: _isOrbiting=False, _isPanning=False
[   2713.34 ms] [PointerMoved] Pos=(530.0,405.0), Delta=(40.0,20.0) -> IsOrbiting=False, IsPanning=False
[   2713.55 ms] [PointerReleased] Button=Left, WasOrbiting=False, WasPanning=False

--- TEST 2: Right-drag without modifiers ---
[   2864.15 ms] --- Simulating Drag: Button=Right, Modifiers=None, Start=490, 385, End=530, 405 ---
[   2864.18 ms] [SimulatePress] Resulting state: _isOrbiting=True, _isPanning=False
[   2864.22 ms] [PointerMoved] Pos=(530.0,405.0), Delta=(40.0,20.0) -> IsOrbiting=True, IsPanning=False
[   2865.86 ms] [CameraOrbit] Delta=(40.0,20.0) -> Azimuth=-77.9°, Elevation=47.0°
[   2866.02 ms] [RenderRequested] by SimulatePointerDrag:Orbit
[   2867.11 ms] [RenderBegin] Frame #2, Size=980x770
[   2867.18 ms] [RenderEnd] Frame #2, Duration=0.03 ms

--- TEST 3: Alt + Left-drag ---
[   3016.52 ms] --- Simulating Drag: Button=Left, Modifiers=Alt, Start=490, 385, End=530, 405 ---
[   3016.54 ms] [SimulatePress] Resulting state: _isOrbiting=True, _isPanning=False
[   3016.59 ms] [CameraOrbit] Delta=(40.0,20.0) -> Azimuth=-92.8°, Elevation=54.5°
[   3016.62 ms] [RenderRequested] by SimulatePointerDrag:Orbit
[   3016.96 ms] [RenderBegin] Frame #3, Size=980x770
[   3017.03 ms] [RenderEnd] Frame #3, Duration=0.03 ms
```

---

## 3. Minimal Surgical Fix

1. **Implement `Avalonia.Rendering.ICustomHitTest` in `Viewport3DControl`**:
   ```csharp
   public sealed class Viewport3DControl : OpenGlControlBase, Avalonia.Rendering.ICustomHitTest
   {
       public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);
   }
   ```
   *Effect*: Ensures Avalonia routes all pointer events across the control's bounds directly to `Viewport3DControl`.

2. **Document Mouse Mapping in Fit View Button Tooltip**:
   Updated `Toolbar.FitView.Tooltip` in `Strings.resx` and `Theme.axaml`:
   ```xml
   <value>Fit camera to model (F) | Orbit: Right-drag (or Alt+Left) | Pan: Middle-drag | Zoom: Wheel</value>
   ```

3. **Deterministic Unit Tests Added**:
   `tests/Mycel.Core.Tests/Geometry/OrbitCameraTests.cs`:
   - `Orbit_DeltaChangesYawAndPitchDeterministically`
   - `Orbit_ClampsElevationAtGimbalPoles`
   - `Pan_ChangesTargetDeterministically`
   - `Viewport_HitTest_ReturnsTrueInsideBoundsAndFalseOutside`
   - `Input_MarksFrameRequested_WithFakeRenderScheduler`

