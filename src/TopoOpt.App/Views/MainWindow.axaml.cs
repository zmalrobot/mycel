using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TopoOpt.App.ViewModels;
using TopoOpt.Core.Geometry;

namespace TopoOpt.App.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;
    private int _lastHoverSeedTriangle = -1;
    private float _lastHoverAngle = -1f;
    private int[] _currentHoveredTriangles = [];

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

        vm.SelectionOverlayUpdated += item =>
        {
            Viewport.SetOverlayTriangles(item.Id, item.Model.TriangleIndices, item.ColorRgba);
        };

        vm.SelectionOverlayRemoved += id =>
        {
            Viewport.RemoveOverlay(id);
        };

        Viewport.ViewportPointerMoved += OnViewportPointerMoved;
        Viewport.ViewportPointerClicked += OnViewportPointerClicked;
        Viewport.DiagnosticsUpdated += vm.UpdateDiagnostics;

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

        if (args.Length > 0 && File.Exists(args[0]))
        {
            await ViewModel.LoadStlFileAsync(args[0], ViewModel.SelectedUnit);
        }
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

    private void OnFitViewClick(object? sender, RoutedEventArgs e)
    {
        Viewport.FitView();
    }

    private void OnCycleColorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SelectionItemViewModel item })
        {
            ViewModel.CycleColor(item);
        }
    }

    private void OnRemoveSelectionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SelectionItemViewModel item })
        {
            ViewModel.RemoveSelection(item);
        }
    }

    private void OnViewportPointerMoved(Vector2 pixelPos)
    {
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
                ViewModel.ApplyClickedRegion([], SelectionModifyMode.Replace);
            }
            return;
        }

        int[] region = selector.GrowRegion(hit.TriangleIndex, ViewModel.AngleThresholdDegrees);
        ViewModel.ApplyClickedRegion(region, mode);
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
}

