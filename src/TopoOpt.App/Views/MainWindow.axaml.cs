using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TopoOpt.App.Diagnostics;
using TopoOpt.App.ViewModels;
using TopoOpt.Core.Geometry;

namespace TopoOpt.App.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;
    private int _lastHoverSeedTriangle = -1;
    private float _lastHoverAngle = -1f;
    private int[] _currentHoveredTriangles = [];
    private bool _allowClose;

    public MainWindow() : this([])
    {
    }

    public MainWindow(string[] args)
    {
        InitializeComponent();

        MainWindowViewModel vm = new();
        DataContext = vm;

        vm.MeshLoaded += mesh =>
        {
            _lastHoverSeedTriangle = -1;
            _currentHoveredTriangles = [];
            Viewport.SetMesh(mesh);
        };

        vm.GhostOriginalMeshUpdated += (mesh, visible) =>
        {
            Viewport.SetGhostOriginalMesh(mesh, visible);
        };

        vm.ShowGhostOriginalChanged += visible =>
        {
            Viewport.SetShowGhostOriginal(visible);
        };

        vm.AsyncIsoMeshUpdated += mesh => Viewport.UpdateMeshAsync(mesh);

        vm.ComplianceChartUpdated += () =>
        {
            ComplianceChart.NotifyDataUpdated();
        };

        ComplianceChart.IterationScrubbed += it =>
        {
            vm.ScrubFromChart(it);
        };

        vm.SelectionOverlayUpdated += (id, triangles, color) =>
        {
            Viewport.SetOverlayTriangles(id, triangles.Span, color);
        };

        vm.SelectionOverlayRemoved += id =>
        {
            Viewport.RemoveOverlay(id);
        };

        vm.AllOverlaysCleared += () =>
        {
            Viewport.ClearOverlays();
        };

        vm.GlyphsUpdated += (glyphs, diag) =>
        {
            Viewport.SetBoundaryGlyphs(glyphs, diag);
        };

        vm.FeaScalarFieldUpdated += (scalars, lut, min, max) =>
        {
            Viewport.SetFeaScalarField(scalars.Span, lut, min, max);
        };

        vm.FeaScalarFieldCleared += () =>
        {
            Viewport.ClearFeaScalarField();
        };

        Viewport.ViewportPointerMoved += OnViewportPointerMoved;
        Viewport.ViewportPointerClicked += OnViewportPointerClicked;
        Viewport.DiagnosticsUpdated += vm.UpdateDiagnostics;
        Viewport.GlInitFailedNotification += vm.HandleGlFailure;

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        Opened += async (_, _) => await HandleStartupArgsAsync(args);
    }

    private async Task HandleStartupArgsAsync(string[] args)
    {
        if (args.Length > 0 && args[0] == "--smoke-500k")
        {
            await RunSmokeTest500kAsync();
            return;
        }

        if (args.Length > 0 && Array.Exists(args, a => string.Equals(a, "--test-orbit", StringComparison.OrdinalIgnoreCase)))
        {
            await RunOrbitDiagnosticTestAsync();
            return;
        }

        if (Array.Exists(args, a => string.Equals(a, "--test-stepper", StringComparison.OrdinalIgnoreCase)))
        {
            await RunStepperDiagnosticTestAsync(Array.Exists(args, a => string.Equals(a, "--diag-pointer", StringComparison.OrdinalIgnoreCase)));
            return;
        }

        int scenarioIdx = Array.FindIndex(args, a => string.Equals(a, "--scenario", StringComparison.OrdinalIgnoreCase));
        if (scenarioIdx >= 0)
        {
            string scenarioName = (scenarioIdx + 1 < args.Length && !args[scenarioIdx + 1].StartsWith("--"))
                ? args[scenarioIdx + 1]
                : "obj_1_Assemblaggio";
            await RunScenarioInWindowAsync(scenarioName);
            return;
        }

        if (args.Length > 0 && File.Exists(args[0]))
        {
            await ViewModel.LoadStlFileAsync(args[0], ViewModel.SelectedUnit);
        }
    }

    private async Task RunScenarioInWindowAsync(string scenarioName)
    {
        Trace.WriteLine($"=== Running Scenario '{scenarioName}' in Live Window ===");
        await Task.Delay(300);

        string stlPath;
        if (scenarioName.StartsWith("stress-", StringComparison.OrdinalIgnoreCase))
        {
            string baseStl = Path.Combine(AppContext.BaseDirectory, "examples", "obj_1_Assemblaggio.stl");
            if (!File.Exists(baseStl)) baseStl = "examples/obj_1_Assemblaggio.stl";
            using (var fs = File.OpenRead(baseStl))
            {
                var (baseMesh, _) = StlReader.Read(fs);
                int targetTris = scenarioName switch
                {
                    "stress-100k" => 100_000,
                    "stress-500k" => 500_000,
                    "stress-1m" => 1_000_000,
                    _ => 100_000
                };
                var subdivided = StressMeshGenerator.Subdivide(baseMesh, targetTris);
                stlPath = StressMeshGenerator.WriteToTempStl(subdivided, $"{scenarioName}.stl");
            }
        }
        else
        {
            stlPath = Path.Combine(AppContext.BaseDirectory, "examples", "obj_1_Assemblaggio.stl");
            if (!File.Exists(stlPath)) stlPath = "examples/obj_1_Assemblaggio.stl";
        }

        var result = await ScenarioRunner.RunPipelineScenarioAsync(ViewModel, stlPath, scenarioName);

        string summary = $"=== Scenario '{scenarioName}' Result: Success={result.Success}, Triangles={result.TriangleCount:N0}, TotalTime={result.TotalTimeMs:F1}ms, OverallMaxUiGap={result.OverallMaxUiGapMs:F1}ms ===";
        Trace.WriteLine(summary);
        Console.WriteLine(summary);
        try { File.AppendAllText(Path.Combine(CrashLogger.LogDir, "scenario.log"), $"[{DateTime.UtcNow:O}] {summary}\n"); } catch { }
        foreach (var (step, dur) in result.StepDurationsMs)
        {
            double gap = result.StepMaxUiGapsMs.TryGetValue(step, out double g) ? g : 0;
            string line = $"  * {step}: {dur:F1} ms (Max UI gap: {gap:F1} ms)";
            Trace.WriteLine(line);
            Console.WriteLine(line);
            try { File.AppendAllText(Path.Combine(CrashLogger.LogDir, "scenario.log"), $"[{DateTime.UtcNow:O}] {line}\n"); } catch { }
        }

        if (Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, "--exit-after-scenario", StringComparison.OrdinalIgnoreCase)))
        {
            await Task.Delay(500);
            Close();
        }
    }

    /// <summary>
    /// Debug-only probe for the left stepper: logs hit-testing of the step headers, the real visibility
    /// of each step body after SelectStep, and the result of the ObjectConverters.Equal comparison.
    /// With --diag-pointer it also keeps the window open and logs every pointer event (manual run).
    /// </summary>
    private async Task RunStepperDiagnosticTestAsync(bool pointerLogging)
    {
        string logPath = Path.Combine(Diagnostics.CrashLogger.LogDir, "stepper_diag.log");
        void L(string m)
        {
            string line = $"[{DateTime.UtcNow:O}] {m}";
            Console.WriteLine(line);
            try { File.AppendAllText(logPath, line + Environment.NewLine); } catch { }
        }

        L("=== Stepper diagnostic start ===");
        await Task.Delay(500);

        string stl = Path.Combine(AppContext.BaseDirectory, "examples", "obj_1_Assemblaggio.stl");
        if (!File.Exists(stl)) stl = "examples/obj_1_Assemblaggio.stl";
        await ViewModel.LoadStlFileAsync(stl, LengthUnit.Millimeter);
        await Task.Delay(300);

        Button[] headers = [Step1Header, Step2Header, Step3Header, Step4Header, Step5Header, Step6Header];
        Control[] bodies = [Step1Body, Step2Body, Step3Body, Step4Body, Step5Body, Step6Body];

        L($"After load: ActiveStep={ViewModel.ActiveStep}");
        for (int i = 0; i < 6; i++)
        {
            Point? origin = headers[i].TranslatePoint(new Point(headers[i].Bounds.Width / 2, headers[i].Bounds.Height / 2), this);
            var hit = origin is { } c ? this.InputHitTest(c) : null;
            L($"Header{i + 1}: center={origin} enabled={headers[i].IsEffectivelyEnabled} hitTestVisible={headers[i].IsHitTestVisible} bounds={headers[i].Bounds} hit={hit?.GetType().FullName ?? "null"}");
        }

        object? conv = Avalonia.Data.Converters.ObjectConverters.Equal.Convert(1, typeof(bool), "1", System.Globalization.CultureInfo.InvariantCulture);
        L($"ObjectConverters.Equal.Convert(int 1, \"1\") = {conv}");
        object? conv2 = Avalonia.Data.Converters.ObjectConverters.Equal.Convert(1, typeof(bool), 1, System.Globalization.CultureInfo.InvariantCulture);
        L($"ObjectConverters.Equal.Convert(int 1, int 1) = {conv2}");

        for (int n = 1; n <= 6; n++)
        {
            ViewModel.SelectStepFromHeaderCommand.Execute(n.ToString());
            await Task.Delay(120);
            string vis = string.Join(", ", bodies.Select((b, i) => $"B{i + 1}={b.IsVisible}"));
            L($"SelectStep({n}) -> ActiveStep={ViewModel.ActiveStep}; {vis}");
        }

        // Scan every button in the window: report the ones that are disabled and why (command CanExecute vs IsEnabled).
        foreach (var btn in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(this).OfType<Button>())
        {
            if (!btn.IsEffectivelyEnabled)
            {
                bool? canExec = btn.Command?.CanExecute(btn.CommandParameter);
                L($"DISABLED button: name='{btn.Name}' content='{btn.Content?.ToString() ?? btn.Content?.GetType().Name}' visible={btn.IsEffectivelyVisible} isEnabled={btn.IsEnabled} command={(btn.Command is null ? "none" : "yes")} canExecute={canExec} param={btn.CommandParameter ?? "null"} ({btn.CommandParameter?.GetType().Name})");
            }
        }

        if (pointerLogging)
        {
            L("Pointer logging enabled: hover and click the left buttons for ~20 s.");
            int count = 0;
            AddHandler(PointerMovedEvent, (_, e) =>
            {
                if (count++ % 15 != 0) return;
                L($"Moved window={e.GetPosition(this)} source={(e.Source as Control)?.GetType().Name}/{(e.Source as Control)?.Name} captured={e.Pointer.Captured?.GetType().Name ?? "none"}");
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
            AddHandler(PointerPressedEvent, (_, e) =>
                L($"Pressed window={e.GetPosition(this)} source={(e.Source as Control)?.GetType().Name}/{(e.Source as Control)?.Name} captured={e.Pointer.Captured?.GetType().Name ?? "none"}"),
                Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
            await Task.Delay(TimeSpan.FromSeconds(25));
        }

        L("=== Stepper diagnostic end ===");
        Close();
    }

    private async Task RunOrbitDiagnosticTestAsync()
    {
        OrbitDiagnostics.Log("=== Starting Orbit Diagnostic Test in Live Window ===");
        await Task.Delay(300); // Allow window and OpenGL to initialize

        OrbitDiagnostics.Log($"Viewport Bounds: {Viewport.Bounds.Width}x{Viewport.Bounds.Height}");
        OrbitDiagnostics.Log($"GL Initialized: {Viewport.IsGlInitialized}, GL Init Failed: {Viewport.GlInitFailed}");

        // Hit-test check
        OrbitDiagnostics.Log("\n--- TEST: Hit-test check on Viewport at center ---");
        Point center = new(Viewport.Bounds.Width / 2.0, Viewport.Bounds.Height / 2.0);
        var hitWindow = this.InputHitTest(center);
        var hitViewport = Viewport.InputHitTest(new Point(100, 100));
        OrbitDiagnostics.Log($"this.InputHitTest at {center} returned: {hitWindow?.GetType().FullName ?? "null"} (Name={((hitWindow as Control)?.Name ?? "none")})");
        OrbitDiagnostics.Log($"Viewport.InputHitTest at (100, 100) returned: {hitViewport?.GetType().FullName ?? "null"} (Name={((hitViewport as Control)?.Name ?? "none")})");

        // Test 1: Simulate Left-drag (no modifiers)
        OrbitDiagnostics.Log("\n--- TEST 1: Left-drag without modifiers ---");
        Viewport.DiagnosticSimulatePointerDrag(MouseButton.Left, KeyModifiers.None, center, center + new Point(40, 20));
        await Task.Delay(150);

        // Test 2: Simulate Right-drag
        OrbitDiagnostics.Log("\n--- TEST 2: Right-drag without modifiers ---");
        Viewport.DiagnosticSimulatePointerDrag(MouseButton.Right, KeyModifiers.None, center, center + new Point(40, 20));
        await Task.Delay(150);

        // Test 3: Simulate Alt + Left-drag
        OrbitDiagnostics.Log("\n--- TEST 3: Alt + Left-drag ---");
        Viewport.DiagnosticSimulatePointerDrag(MouseButton.Left, KeyModifiers.Alt, center, center + new Point(40, 20));
        await Task.Delay(150);

        OrbitDiagnostics.Log("=== Orbit Diagnostic Test Finished ===");
        await Task.Delay(300);
        Close();
    }

    private async Task RunSmokeTest500kAsync()
    {
        // 500x501 sphere has 2 * 501 * 499 = 500,000 triangles
        var (mesh, bvh, selector, report) = await Task.Run(() =>
        {
            int lat = 500, lon = 501;
            float r = 40f;
            Vector3[] pos = new Vector3[2 + (lat - 1) * lon];
            pos[0] = new Vector3(0f, 0f, +r);
            pos[1] = new Vector3(0f, 0f, -r);
            int idx = 2;
            for (int i = 1; i < lat; i++)
            {
                double phi = Math.PI * i / lat;
                float z = (float)(r * Math.Cos(phi));
                float rr = (float)(r * Math.Sin(phi));
                for (int j = 0; j < lon; j++)
                {
                    double th = 2.0 * Math.PI * j / lon;
                    pos[idx++] = new Vector3((float)(rr * Math.Cos(th)), (float)(rr * Math.Sin(th)), z);
                }
            }

            int[] indices = new int[2 * lon * (lat - 1) * 3];
            int t = 0;
            for (int j = 0; j < lon; j++)
            {
                int next = (j + 1) % lon;
                indices[t++] = 0; indices[t++] = 2 + j; indices[t++] = 2 + next;
            }
            for (int i = 0; i < lat - 2; i++)
            {
                int row0 = 2 + i * lon;
                int row1 = 2 + (i + 1) * lon;
                for (int j = 0; j < lon; j++)
                {
                    int next = (j + 1) % lon;
                    indices[t++] = row0 + j; indices[t++] = row1 + j; indices[t++] = row1 + next;
                    indices[t++] = row0 + j; indices[t++] = row1 + next; indices[t++] = row0 + next;
                }
            }
            int lastRow = 2 + (lat - 2) * lon;
            for (int j = 0; j < lon; j++)
            {
                int next = (j + 1) % lon;
                indices[t++] = 1; indices[t++] = lastRow + next; indices[t++] = lastRow + j;
            }

            IndexedMesh m = new(pos, indices);
            Bvh b = new(m);
            RegionSelector s = new(m);
            MeshReport rep = new(m.TriangleCount, m.VertexCount, 0, 0, 0, 0, true, m.SignedVolume, m.BBox, LengthUnit.Millimeter, TimeSpan.Zero);
            return (m, b, s, rep);
        });

        ViewModel.ApplyLoadedMesh(mesh, bvh, selector, report);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int frames = 0;
        while (sw.Elapsed.TotalSeconds < 2.0)
        {
            Viewport.Camera.Orbit(4.0f, 1.0f);
            Viewport.RequestRedraw();
            frames++;
            await Task.Delay(8);
        }

        Console.WriteLine("=== GPU Viewport Smoke Test (500k Triangles) ===");
        Console.WriteLine($"  GL Profile:     {Viewport.GlProfileSummary}");
        Console.WriteLine($"  Measured FPS:   {Viewport.CurrentFps:F1} FPS");
        Close();
    }

    private async void OnOpenStlClick(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open STL Model",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("STL Files") { Patterns = ["*.stl", "*.STL"] },
                FilePickerFileTypes.All
            ]
        });

        if (files.Count > 0)
        {
            string? localPath = files[0].TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
            {
                await ViewModel.LoadStlFileAsync(localPath, ViewModel.SelectedUnit);
            }
        }
    }

    private async void OnOpenProjectClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open Project Folder"
        });

        if (folders.Count > 0)
        {
            string? localPath = folders[0].TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(localPath))
            {
                await ViewModel.OpenProjectAsync(localPath);
            }
        }
    }

    private async void OnSaveProjectClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.CurrentProjectPath))
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Destination Folder for Project"
            });

            if (folders.Count > 0)
            {
                string? localPath = folders[0].TryGetLocalPath();
                if (!string.IsNullOrWhiteSpace(localPath))
                {
                    await ViewModel.SaveProjectAsync(localPath);
                }
            }
        }
        else
        {
            await ViewModel.SaveProjectAsync();
        }
    }

    private void OnFitViewClick(object? sender, RoutedEventArgs e)
    {
        Viewport.FitView();
    }

    private void OnViewportPointerMoved(Vector2 pixelPos)
    {
        if (!ViewModel.IsPickingActive)
        {
            if (_lastHoverSeedTriangle != -1)
            {
                _lastHoverSeedTriangle = -1;
                _currentHoveredTriangles = [];
                Viewport.SetHoverTriangles([]);
            }
            return;
        }

        Bvh? bvh = ViewModel.CurrentBvh;
        RegionSelector? selector = ViewModel.CurrentSelector;
        if (bvh is null || selector is null)
        {
            return;
        }

        float w = (float)Viewport.Bounds.Width;
        float h = (float)Viewport.Bounds.Height;
        if (w <= 1f || h <= 1f)
        {
            return;
        }

        var (origin, dir) = Viewport.Camera.UnprojectRay(pixelPos.X, pixelPos.Y, w, h);
        RayHit hit = bvh.RayCast(origin, dir);

        if (!hit.IsHit)
        {
            if (_lastHoverSeedTriangle != -1)
            {
                _lastHoverSeedTriangle = -1;
                _currentHoveredTriangles = [];
                Viewport.SetHoverTriangles([]);
            }
            return;
        }

        float angle = ViewModel.AngleThresholdDegrees;
        if (hit.TriangleIndex == _lastHoverSeedTriangle && MathF.Abs(angle - _lastHoverAngle) < 1e-3f)
        {
            return;
        }

        _lastHoverSeedTriangle = hit.TriangleIndex;
        _lastHoverAngle = angle;
        _currentHoveredTriangles = selector.GrowRegion(hit.TriangleIndex, angle);
        Viewport.SetHoverTriangles(_currentHoveredTriangles);
    }

    private void OnViewportPointerClicked(Vector2 pixelPos, KeyModifiers modifiers)
    {
        if (!ViewModel.IsPickingActive) return;

        Bvh? bvh = ViewModel.CurrentBvh;
        RegionSelector? selector = ViewModel.CurrentSelector;
        if (bvh is null || selector is null)
        {
            return;
        }

        float w = (float)Viewport.Bounds.Width;
        float h = (float)Viewport.Bounds.Height;
        var (origin, dir) = Viewport.Camera.UnprojectRay(pixelPos.X, pixelPos.Y, w, h);
        RayHit hit = bvh.RayCast(origin, dir);

        SelectionModifyMode mode = SelectionModifyMode.Replace;
        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            mode = SelectionModifyMode.Add;
        }
        else if (modifiers.HasFlag(KeyModifiers.Control))
        {
            mode = SelectionModifyMode.Subtract;
        }

        if (!hit.IsHit)
        {
            if (mode == SelectionModifyMode.Replace)
            {
                ViewModel.ModifyActiveSelectionTriangles([], SelectionModifyMode.Replace);
            }
            return;
        }

        int[] region = selector.GrowRegion(hit.TriangleIndex, ViewModel.AngleThresholdDegrees);
        ViewModel.ModifyActiveSelectionTriangles(region, mode);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && ViewModel.IsPickingActive)
        {
            ViewModel.CancelPicking();
            e.Handled = true;
        }
        else if (e.Key == Key.F)
        {
            Viewport.FitView();
            e.Handled = true;
        }
        else if (e.Key == Key.O && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            OnOpenStlClick(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        foreach (IStorageItem item in e.DataTransfer.TryGetFiles() ?? [])
        {
            string? path = item.TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path) &&
                path.EndsWith(".stl", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(path))
            {
                await ViewModel.LoadStlFileAsync(path, ViewModel.SelectedUnit);
                break;
            }
        }
    }

    private async void OnOpenRunClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open Optimisation Run Folder"
        });

        if (folders.Count > 0)
        {
            string? localPath = folders[0].TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(localPath))
            {
                await ViewModel.OpenRunFolderAsync(localPath);
            }
        }
    }

    private async void OnExportStlClick(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Optimised Iso-Surface as Binary STL",
            DefaultExtension = "stl",
            SuggestedFileName = $"optimised_it{ViewModel.SelectedIteration:D4}.stl",
            FileTypeChoices =
            [
                new FilePickerFileType("Binary STL File") { Patterns = ["*.stl"] }
            ]
        });

        string? localPath = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            await ViewModel.ExportCurrentIterationStlAsync(localPath);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (ViewModel.IsOptimising && !_allowClose)
        {
            e.Cancel = true;
            CloseConfirmOverlay.IsVisible = true;
            return;
        }

        base.OnClosing(e);
    }

    private async void OnConfirmStopAndExitClick(object? sender, RoutedEventArgs e)
    {
        CloseConfirmOverlay.IsVisible = false;
        await ViewModel.StopAndFinalizeActiveRunAsync();
        _allowClose = true;
        Close();
    }

    private void OnCancelCloseClick(object? sender, RoutedEventArgs e)
    {
        CloseConfirmOverlay.IsVisible = false;
    }
}

