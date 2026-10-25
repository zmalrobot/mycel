using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TopoOpt.App.Controls;
using TopoOpt.Core.Geometry;
using TopoOpt.Core.Voxelization;

namespace TopoOpt.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _voxelCts;
    private int _selectionCounter = 1;
    private MeshReport? _currentReport;

    public IndexedMesh? CurrentMesh { get; private set; }
    public Bvh? CurrentBvh { get; private set; }
    public RegionSelector? CurrentSelector { get; private set; }
    public VoxelModel? CurrentVoxelModel { get; private set; }
    public IndexedMesh? CurrentVoxelMesh { get; private set; }

    public ObservableCollection<SelectionItemViewModel> Selections { get; } = new();
    public ObservableCollection<ValidationIssue> ValidationIssues { get; } = new();

    public SelectionKind[] AvailableKinds { get; } =
    [
        SelectionKind.Load,
        SelectionKind.Support,
        SelectionKind.Preserve
    ];

    public LengthUnit[] AvailableUnits { get; } =
    [
        LengthUnit.Millimeter,
        LengthUnit.Centimeter,
        LengthUnit.Meter,
        LengthUnit.Inch
    ];

    public int[] TargetElementPresets { get; } =
    [
        50_000,
        100_000,
        250_000,
        500_000
    ];

    public ColorOption[] PaletteColors { get; } =
    [
        new("Coral Red", 0xFF5252FFu, SelectionItemViewModel.ToBrush(0xFF5252FFu)),
        new("Royal Blue", 0x3B82F6FFu, SelectionItemViewModel.ToBrush(0x3B82F6FFu)),
        new("Emerald", 0x10B981FFu, SelectionItemViewModel.ToBrush(0x10B981FFu)),
        new("Amber", 0xF59E0BFFu, SelectionItemViewModel.ToBrush(0xF59E0BFFu)),
        new("Violet", 0x8B5CF6FFu, SelectionItemViewModel.ToBrush(0x8B5CF6FFu)),
        new("Cyan", 0x06B6D4FFu, SelectionItemViewModel.ToBrush(0x06B6D4FFu))
    ];

    [ObservableProperty]
    private SelectionItemViewModel? _activeSelection;

    [ObservableProperty]
    private float _angleThresholdDegrees = 30.0f;

    [ObservableProperty]
    private LengthUnit _selectedUnit = LengthUnit.Millimeter;

    [ObservableProperty]
    private int _targetSolidElements = 100_000;

    [ObservableProperty]
    private ViewRenderMode _currentViewMode = ViewRenderMode.OriginalStl;

    [ObservableProperty]
    private bool _isVoxelising;

    [ObservableProperty]
    private float _voxelProgress;

    [ObservableProperty]
    private string _voxelStatusText = "Not Voxelised";

    [ObservableProperty]
    private bool _hasValidationErrors;

    [ObservableProperty]
    private string _statusTriangles = "0 tris";

    [ObservableProperty]
    private string _statusBBox = "BBox: —";

    [ObservableProperty]
    private string _watertightText = "No Mesh";

    [ObservableProperty]
    private IBrush _watertightBadgeBrush = new SolidColorBrush(Color.FromRgb(75, 85, 99));

    [ObservableProperty]
    private string _fpsText = "0 FPS";

    [ObservableProperty]
    private bool _isLoading;

    public event Action<IndexedMesh?>? MeshLoaded;
    public event Action<IndexedMesh?>? VoxelMeshUpdated;
    public event Action<Vector3[], uint>? ActivePatchNodesUpdated;
    public event Action<ViewRenderMode>? ViewRenderModeChanged;
    public event Action<SelectionItemViewModel>? SelectionOverlayUpdated;
    public event Action<Guid>? SelectionOverlayRemoved;

    [RelayCommand]
    public void AddSelection()
    {
        SelectionKind kind = (Selections.Count % 3) switch
        {
            0 => SelectionKind.Support,
            1 => SelectionKind.Load,
            _ => SelectionKind.Preserve
        };

        SurfaceSelection model = new()
        {
            Kind = kind,
            Name = $"{kind} {_selectionCounter++}",
            ColorRgba = SurfaceSelection.DefaultColorForKind(kind),
            TriangleIndices = []
        };

        SelectionItemViewModel vm = new(model);
        vm.OverlayChanged += OnItemOverlayChanged;
        Selections.Add(vm);
        ActiveSelection = vm;
    }

    [RelayCommand]
    public void RemoveSelection(SelectionItemViewModel? item)
    {
        if (item is null) return;
        item.OverlayChanged -= OnItemOverlayChanged;
        Selections.Remove(item);
        SelectionOverlayRemoved?.Invoke(item.Id);

        if (ActiveSelection == item)
        {
            ActiveSelection = Selections.Count > 0 ? Selections[^1] : null;
        }
    }

    [RelayCommand]
    public void CycleColor(SelectionItemViewModel? item)
    {
        if (item is null) return;
        int idx = 0;
        for (int i = 0; i < PaletteColors.Length; i++)
        {
            if (PaletteColors[i].Rgba == item.ColorRgba)
            {
                idx = (i + 1) % PaletteColors.Length;
                break;
            }
        }
        item.ColorRgba = PaletteColors[idx].Rgba;
    }

    public async Task LoadStlFileAsync(string filePath, LengthUnit unit)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        CancellationToken ct = _loadCts.Token;

        IsLoading = true;
        try
        {
            float scale = unit.ToMillimetersScale();
            var (mesh, bvh, selector, report) = await Task.Run(() =>
            {
                using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
                var (m, r) = StlReader.Read(fs, scaleFactor: scale, ct: ct);
                Bvh b = new(m);
                RegionSelector s = new(m);
                return (m, b, s, r);
            }, ct);

            ApplyLoadedMesh(mesh, bvh, selector, report);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void ApplyLoadedMesh(IndexedMesh mesh, Bvh bvh, RegionSelector selector, MeshReport report)
    {
        CurrentMesh = mesh;
        CurrentBvh = bvh;
        CurrentSelector = selector;

        foreach (SelectionItemViewModel s in Selections)
        {
            s.OverlayChanged -= OnItemOverlayChanged;
        }
        Selections.Clear();
        ActiveSelection = null;
        _selectionCounter = 1;

        Vector3 size = report.BBox.Max - report.BBox.Min;
        StatusTriangles = $"{report.Triangles:N0} tris ({report.Vertices:N0} verts)";
        StatusBBox = $"{size.X:F1} × {size.Y:F1} × {size.Z:F1} mm";

        if (report.IsWatertight)
        {
            WatertightText = "Watertight";
            WatertightBadgeBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
        }
        else
        {
            WatertightText = $"Open ({report.OpenEdges} open, {report.NonManifoldEdges} non-manifold)";
            WatertightBadgeBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
        }

        _currentReport = report;
        CurrentVoxelModel = null;
        CurrentVoxelMesh = null;
        VoxelStatusText = "Not Voxelised";
        ValidationIssues.Clear();
        HasValidationErrors = false;
        VoxelMeshUpdated?.Invoke(null);
        ActivePatchNodesUpdated?.Invoke([], 0);

        // Create initial default selection slot so clicking immediately selects
        AddSelection();
        MeshLoaded?.Invoke(mesh);
    }

    partial void OnCurrentViewModeChanged(ViewRenderMode value)
    {
        ViewRenderModeChanged?.Invoke(value);
    }

    partial void OnActiveSelectionChanged(SelectionItemViewModel? value)
    {
        UpdateActivePatchNodes();
    }

    [RelayCommand]
    public async Task VoxeliseAsync()
    {
        if (CurrentMesh is null) return;

        _voxelCts?.Cancel();
        _voxelCts = new CancellationTokenSource();
        CancellationToken ct = _voxelCts.Token;

        IsVoxelising = true;
        VoxelProgress = 0f;
        VoxelStatusText = "Voxelising...";

        Stopwatch sw = Stopwatch.StartNew();

        try
        {
            var progress = new Progress<float>(p => VoxelProgress = p * 100f);

            var (model, vMesh) = await Task.Run(() =>
            {
                var grid = GridSpec.FromTargetElementCount(CurrentMesh, TargetSolidElements, padding: 1);
                var (m, _) = Voxeliser.Voxelise(CurrentMesh, grid, report: _currentReport, progress: progress, cancellationToken: ct);
                var exposedMesh = VoxelMeshBuilder.BuildExposedMesh(m);
                return (m, exposedMesh);
            }, ct);

            sw.Stop();
            CurrentVoxelModel = model;
            CurrentVoxelMesh = vMesh;

            VoxelStatusText = $"{model.SolidCount:N0} solid voxels ({sw.ElapsedMilliseconds} ms, {model.SolidVolume:N1} mm³)";
            VoxelMeshUpdated?.Invoke(vMesh);

            UpdateValidation();
            UpdateActivePatchNodes();

            // Auto-switch to voxel view if requested
            CurrentViewMode = ViewRenderMode.Voxels;
        }
        catch (OperationCanceledException)
        {
            VoxelStatusText = "Voxelisation cancelled";
        }
        catch (Exception ex)
        {
            VoxelStatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsVoxelising = false;
        }
    }

    [RelayCommand]
    public void CancelVoxelise()
    {
        _voxelCts?.Cancel();
    }

    public void UpdateValidation()
    {
        ValidationIssues.Clear();

        if (CurrentVoxelModel is null || CurrentMesh is null)
        {
            HasValidationErrors = false;
            return;
        }

        var list = ModelValidator.Validate(
            CurrentVoxelModel,
            CurrentMesh,
            Selections.Select(s => s.Model).ToList());

        foreach (var issue in list)
        {
            ValidationIssues.Add(issue);
        }

        HasValidationErrors = list.Any(i => i.Severity == ValidationSeverity.Error);
    }

    private void UpdateActivePatchNodes()
    {
        if (CurrentVoxelModel is null || CurrentMesh is null || ActiveSelection is null || ActiveSelection.TriangleCount == 0)
        {
            ActivePatchNodesUpdated?.Invoke([], 0);
            return;
        }

        var patch = SurfacePatchBuilder.Build(CurrentVoxelModel, CurrentMesh, ActiveSelection.Model.TriangleIndices);
        var grid = CurrentVoxelModel.Grid;

        Vector3[] nodePositions = new Vector3[patch.Nodes.Length];
        for (int i = 0; i < patch.Nodes.Length; i++)
        {
            grid.InverseNodeIndex(patch.Nodes[i], out int ni, out int nj, out int nk);
            nodePositions[i] = grid.NodePosition(ni, nj, nk);
        }

        ActivePatchNodesUpdated?.Invoke(nodePositions, ActiveSelection.ColorRgba);
    }

    public void ApplyClickedRegion(int[] grownTriangles, SelectionModifyMode mode)
    {
        if (CurrentMesh is null) return;

        if (ActiveSelection is null)
        {
            AddSelection();
        }

        SelectionItemViewModel target = ActiveSelection!;

        if (mode == SelectionModifyMode.Replace &&
            grownTriangles.Length > 0 &&
            target.Model.TriangleIndices.AsSpan().SequenceEqual(grownTriangles))
        {
            // Clicking the exact same region toggles / deselects it
            target.UpdateTriangles([]);
            UpdateActivePatchNodes();
            UpdateValidation();
            return;
        }

        int[] updated = RegionSelector.ModifySelection(target.Model.TriangleIndices, grownTriangles, mode);
        target.UpdateTriangles(updated);
        UpdateActivePatchNodes();
        UpdateValidation();
    }

    public void UpdateDiagnostics(double fps, string glSummary)
    {
        FpsText = $"{fps:F0} FPS";
    }

    private void OnItemOverlayChanged(SelectionItemViewModel item)
    {
        SelectionOverlayUpdated?.Invoke(item);
        if (ActiveSelection == item)
        {
            UpdateActivePatchNodes();
            UpdateValidation();
        }
    }
}

