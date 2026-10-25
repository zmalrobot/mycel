using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TopoOpt.Core.Geometry;

namespace TopoOpt.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private CancellationTokenSource? _loadCts;
    private int _selectionCounter = 1;

    public IndexedMesh? CurrentMesh { get; private set; }
    public Bvh? CurrentBvh { get; private set; }
    public RegionSelector? CurrentSelector { get; private set; }

    public ObservableCollection<SelectionItemViewModel> Selections { get; } = new();

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

        // Create initial default selection slot so clicking immediately selects
        AddSelection();
        MeshLoaded?.Invoke(mesh);
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
            return;
        }

        int[] updated = RegionSelector.ModifySelection(target.Model.TriangleIndices, grownTriangles, mode);
        target.UpdateTriangles(updated);
    }

    public void UpdateDiagnostics(double fps, string glSummary)
    {
        FpsText = $"{fps:F0} FPS";
    }

    private void OnItemOverlayChanged(SelectionItemViewModel item)
    {
        SelectionOverlayUpdated?.Invoke(item);
    }
}

