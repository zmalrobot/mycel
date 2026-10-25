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
using TopoOpt.App.Rendering;
using TopoOpt.Core.Geometry;
using TopoOpt.Core.Voxelization;
using TopoOpt.Core.Workflow;

namespace TopoOpt.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private CancellationTokenSource? _prepareCts;
    private CancellationTokenSource? _analysisCts;
    private readonly DispatcherTimer _autosaveTimer;
    private int _loadCounter = 1;
    private int _supportCounter = 1;

    public WorkflowService Workflow { get; } = new();

    public Bvh? CurrentBvh { get; private set; }
    public RegionSelector? CurrentSelector { get; private set; }

    public ObservableCollection<LoadItemViewModel> Loads { get; } = new();
    public ObservableCollection<SupportItemViewModel> Supports { get; } = new();
    public ObservableCollection<PreserveItemViewModel> Preserves { get; } = new();
    public ObservableCollection<ValidationIssue> ValidationIssues { get; } = new();

    public LengthUnit[] AvailableUnits { get; } =
    [
        LengthUnit.Millimeter,
        LengthUnit.Centimeter,
        LengthUnit.Meter,
        LengthUnit.Inch
    ];

    public MaterialPresetKind[] AvailableMaterialPresets { get; } =
    [
        MaterialPresetKind.Pla,
        MaterialPresetKind.PlaPlus,
        MaterialPresetKind.Petg,
        MaterialPresetKind.Custom
    ];

    public ResolutionPreset[] AvailableResolutionPresets { get; } =
    [
        ResolutionPreset.Draft,
        ResolutionPreset.Standard,
        ResolutionPreset.Fine,
        ResolutionPreset.Custom
    ];

    public LoadDirectionMode[] AvailableLoadDirections { get; } =
    [
        LoadDirectionMode.SurfaceNormalInward,
        LoadDirectionMode.SurfaceNormalOutward,
        LoadDirectionMode.AxisPositiveX,
        LoadDirectionMode.AxisNegativeX,
        LoadDirectionMode.AxisPositiveY,
        LoadDirectionMode.AxisNegativeY,
        LoadDirectionMode.AxisPositiveZ,
        LoadDirectionMode.AxisNegativeZ,
        LoadDirectionMode.CustomVector
    ];

    public LutKind[] AvailableLuts { get; } =
    [
        LutKind.Turbo,
        LutKind.Viridis
    ];

    // Stepper State
    [ObservableProperty]
    private int _activeStep = 1;

    // Model Properties (Step 1)
    [ObservableProperty]
    private string _modelFileName = "No File Loaded";

    [ObservableProperty]
    private LengthUnit _selectedUnit = LengthUnit.Millimeter;

    [ObservableProperty]
    private string _statusTriangles = "0 tris";

    [ObservableProperty]
    private string _statusVertices = "0 verts";

    [ObservableProperty]
    private string _statusBBox = "—";

    [ObservableProperty]
    private string _statusVolume = "—";

    [ObservableProperty]
    private string _watertightText = "No Mesh";

    [ObservableProperty]
    private IBrush _watertightBadgeBrush = new SolidColorBrush(Color.FromRgb(75, 85, 99));

    // Unit Guard Modal
    [ObservableProperty]
    private bool _isUnitGuardOpen;

    [ObservableProperty]
    private string _unitGuardBboxText = string.Empty;

    // Material Properties (Step 4)
    [ObservableProperty]
    private MaterialPresetKind _selectedMaterialPreset = MaterialPresetKind.Pla;

    [ObservableProperty]
    private double _materialE = 3000.0;

    [ObservableProperty]
    private double _materialNu = 0.36;

    [ObservableProperty]
    private double _materialYieldXy = 50.0;

    [ObservableProperty]
    private double _materialYieldZ = 30.0;

    [ObservableProperty]
    private bool _materialConservativeZ;

    // Resolution & Prepare Properties (Step 5)
    [ObservableProperty]
    private ResolutionPreset _selectedResolutionPreset = ResolutionPreset.Standard;

    [ObservableProperty]
    private int _targetSolidElements = 100_000;

    [ObservableProperty]
    private string _voxelSizeHText = "h: —";

    [ObservableProperty]
    private string _estimatedMemoryText = "~100 MB";

    [ObservableProperty]
    private string _estimatedTimeText = "~4.5 s";

    [ObservableProperty]
    private bool _isPreparing;

    [ObservableProperty]
    private float _prepareProgress;

    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private float _analysisProgress;

    // Picking Mode State
    [ObservableProperty]
    private bool _isPickingActive;

    [ObservableProperty]
    private string _pickingBannerText = string.Empty;

    [ObservableProperty]
    private float _angleThresholdDegrees = 30.0f;

    public object? ActivePickingTarget { get; private set; }

    // FEA Result Properties
    [ObservableProperty]
    private bool _hasAnalysisResult;

    [ObservableProperty]
    private bool _isResultOutdated;

    [ObservableProperty]
    private bool _isVonMisesMode = true;

    [ObservableProperty]
    private LutKind _selectedLutKind = LutKind.Turbo;

    [ObservableProperty]
    private string _complianceText = "—";

    [ObservableProperty]
    private string _maxDisplacementText = "—";

    [ObservableProperty]
    private string _maxVonMisesText = "—";

    [ObservableProperty]
    private string _p99VonMisesText = "—";

    [ObservableProperty]
    private string _safetyFactorText = "—";

    [ObservableProperty]
    private string _safetyFactorP99Text = "—";

    [ObservableProperty]
    private IBrush _safetyFactorBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));

    // Legend HUD Properties
    [ObservableProperty]
    private string _legendMinText = "0.0";

    [ObservableProperty]
    private string _legendP99Text = "—";

    [ObservableProperty]
    private string _legendMaxText = "—";

    [ObservableProperty]
    private string _legendUnitsText = "MPa";

    [ObservableProperty]
    private string _fpsText = "0 FPS";

    [ObservableProperty]
    private string _currentProjectPath = string.Empty;

    // Events for Viewport
    public event Action<IndexedMesh?>? MeshLoaded;
    public event Action<IReadOnlyList<Viewport3DControl.BoundaryGlyphData>?, float>? GlyphsUpdated;
    public event Action<ReadOnlyMemory<float>, LutKind, float, float>? FeaScalarFieldUpdated;
    public event Action? FeaScalarFieldCleared;
    public event Action<Guid, ReadOnlyMemory<int>, uint>? SelectionOverlayUpdated;
    public event Action<Guid>? SelectionOverlayRemoved;
    public event Action? AllOverlaysCleared;

    public MainWindowViewModel()
    {
        Workflow.State.StateChanged += OnWorkflowStateChanged;
        Workflow.State.PreparationInvalidated += OnPreparationInvalidated;
        Workflow.State.AnalysisInvalidated += OnAnalysisInvalidated;

        _autosaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _autosaveTimer.Tick += OnAutosaveTick;
    }

    // Step Completion Badges
    public bool IsStep1Completed => Workflow.State.Mesh != null;
    public bool IsStep2Completed => Workflow.State.Loads.Count > 0;
    public bool IsStep3Completed => Workflow.State.Supports.Count > 0;
    public bool IsStep4Completed => Workflow.State.Material != null;
    public bool IsStep5Completed => Workflow.State.AnalysisResult != null && !Workflow.State.AnalysisResult.IsOutdated;
    public bool IsStep5Prepared => Workflow.State.PreparedModel != null && !Workflow.State.PreparedModel.IsOutdated;

    [RelayCommand]
    public void SelectStep(int step)
    {
        ActiveStep = step;
    }

    public async Task LoadStlFileAsync(string filePath, LengthUnit unit = LengthUnit.Millimeter)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return;

        try
        {
            Workflow.LoadStl(filePath, unit);
            ModelFileName = Path.GetFileName(filePath);
            SelectedUnit = unit;

            var mesh = Workflow.State.Mesh!;
            CurrentBvh = new Bvh(mesh);
            CurrentSelector = new RegionSelector(mesh);

            UpdateModelMetricsUI();
            Loads.Clear();
            Supports.Clear();
            Preserves.Clear();
            AllOverlaysCleared?.Invoke();

            MeshLoaded?.Invoke(mesh);
            RefreshBoundaryGlyphs();
            RefreshResolutionMetrics();

            // Check for units guard: max dimension < 1 mm or > 2000 mm
            var (min, max) = mesh.BBox;
            Vector3 size = max - min;
            float maxDim = Math.Max(size.X, Math.Max(size.Y, size.Z));

            if (maxDim < 1.0f || maxDim > 2000.0f)
            {
                UnitGuardBboxText = $"{size.X:F2} x {size.Y:F2} x {size.Z:F2} ({unit})";
                IsUnitGuardOpen = true;
            }
            else
            {
                IsUnitGuardOpen = false;
            }

            ActiveStep = 1;
            NotifyStepBadges();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Error loading STL: {ex.Message}");
        }
    }

    public void ApplyLoadedMesh(IndexedMesh mesh, Bvh bvh, RegionSelector selector, MeshReport report)
    {
        Workflow.LoadMesh(mesh, report);
        CurrentBvh = bvh;
        CurrentSelector = selector;
        ModelFileName = "Synthetic Model";
        UpdateModelMetricsUI();
        Loads.Clear();
        Supports.Clear();
        Preserves.Clear();
        AllOverlaysCleared?.Invoke();
        MeshLoaded?.Invoke(mesh);
        RefreshBoundaryGlyphs();
        RefreshResolutionMetrics();
        ActiveStep = 1;
        NotifyStepBadges();
    }

    [RelayCommand]
    public void AcceptUnitAsMm()
    {
        IsUnitGuardOpen = false;
    }

    [RelayCommand]
    public void RescaleFromMeters()
    {
        Workflow.RescaleMesh(1000.0f, LengthUnit.Millimeter);
        SelectedUnit = LengthUnit.Millimeter;
        UpdateModelMetricsUI();
        CurrentBvh = new Bvh(Workflow.State.Mesh!);
        CurrentSelector = new RegionSelector(Workflow.State.Mesh!);
        MeshLoaded?.Invoke(Workflow.State.Mesh);
        RefreshBoundaryGlyphs();
        RefreshResolutionMetrics();
        IsUnitGuardOpen = false;
    }

    [RelayCommand]
    public void RescaleFromInches()
    {
        Workflow.RescaleMesh(25.4f, LengthUnit.Millimeter);
        SelectedUnit = LengthUnit.Millimeter;
        UpdateModelMetricsUI();
        CurrentBvh = new Bvh(Workflow.State.Mesh!);
        CurrentSelector = new RegionSelector(Workflow.State.Mesh!);
        MeshLoaded?.Invoke(Workflow.State.Mesh);
        RefreshBoundaryGlyphs();
        RefreshResolutionMetrics();
        IsUnitGuardOpen = false;
    }

    [RelayCommand]
    public void AddLoad()
    {
        if (Workflow.State.Mesh == null) return;

        var loadRegion = Workflow.AddLoad($"Load {_loadCounter++}", Array.Empty<int>(), 1000.0f);
        var vm = new LoadItemViewModel(loadRegion);
        vm.Changed += OnLoadItemChanged;
        Loads.Add(vm);
        ActiveStep = 2;
        ArmPicking(vm);
        NotifyStepBadges();
        QueueAutosave();
    }

    [RelayCommand]
    public void RemoveLoad(LoadItemViewModel? item)
    {
        if (item == null) return;
        if (ActivePickingTarget == item) DisarmPicking();

        Workflow.RemoveLoad(item.Id);
        Loads.Remove(item);
        SelectionOverlayRemoved?.Invoke(item.Id);
        RefreshBoundaryGlyphs();
        NotifyStepBadges();
        QueueAutosave();
    }

    [RelayCommand]
    public void AddSupport()
    {
        if (Workflow.State.Mesh == null) return;

        var suppRegion = Workflow.AddSupport($"Support {_supportCounter++}", Array.Empty<int>());
        var vm = new SupportItemViewModel(suppRegion);
        vm.Changed += OnSupportItemChanged;
        Supports.Add(vm);
        ActiveStep = 3;
        ArmPicking(vm);
        NotifyStepBadges();
        QueueAutosave();
    }

    [RelayCommand]
    public void RemoveSupport(SupportItemViewModel? item)
    {
        if (item == null) return;
        if (ActivePickingTarget == item) DisarmPicking();

        Workflow.RemoveSupport(item.Id);
        Supports.Remove(item);
        SelectionOverlayRemoved?.Invoke(item.Id);
        RefreshBoundaryGlyphs();
        NotifyStepBadges();
        QueueAutosave();
    }

    [RelayCommand]
    public void TogglePickLoad(LoadItemViewModel? item)
    {
        if (item == null) return;
        if (IsPickingActive && ActivePickingTarget == item)
        {
            DisarmPicking();
        }
        else
        {
            ArmPicking(item);
        }
    }

    [RelayCommand]
    public void TogglePickSupport(SupportItemViewModel? item)
    {
        if (item == null) return;
        if (IsPickingActive && ActivePickingTarget == item)
        {
            DisarmPicking();
        }
        else
        {
            ArmPicking(item);
        }
    }

    [RelayCommand]
    public void CancelPicking()
    {
        DisarmPicking();
    }

    public void ArmPicking(object target)
    {
        DisarmPicking();
        ActivePickingTarget = target;
        IsPickingActive = true;

        if (target is LoadItemViewModel lvm)
        {
            lvm.IsPicking = true;
            PickingBannerText = $"Selecting surface for '{lvm.Name}' — Click surface (Esc when done)";
        }
        else if (target is SupportItemViewModel svm)
        {
            svm.IsPicking = true;
            PickingBannerText = $"Selecting surface for '{svm.Name}' — Click surface (Esc when done)";
        }
        else if (target is PreserveItemViewModel pvm)
        {
            pvm.IsPicking = true;
            PickingBannerText = $"Selecting surface for '{pvm.Name}' — Click surface (Esc when done)";
        }
    }

    public void DisarmPicking()
    {
        if (ActivePickingTarget is LoadItemViewModel lvm) lvm.IsPicking = false;
        if (ActivePickingTarget is SupportItemViewModel svm) svm.IsPicking = false;
        if (ActivePickingTarget is PreserveItemViewModel pvm) pvm.IsPicking = false;

        ActivePickingTarget = null;
        IsPickingActive = false;
        PickingBannerText = string.Empty;
    }

    // Material Preset changes
    partial void OnSelectedMaterialPresetChanged(MaterialPresetKind value)
    {
        if (value != MaterialPresetKind.Custom)
        {
            var def = MaterialDefinition.CreatePreset(value);
            _materialE = def.E;
            _materialNu = def.Nu;
            _materialYieldXy = def.YieldXy;
            _materialYieldZ = def.YieldZ;

            OnPropertyChanged(nameof(MaterialE));
            OnPropertyChanged(nameof(MaterialNu));
            OnPropertyChanged(nameof(MaterialYieldXy));
            OnPropertyChanged(nameof(MaterialYieldZ));

            SyncMaterialToWorkflow();
        }
    }

    partial void OnMaterialEChanged(double value) => OnMaterialEdited();
    partial void OnMaterialNuChanged(double value) => OnMaterialEdited();
    partial void OnMaterialYieldXyChanged(double value) => OnMaterialEdited();
    partial void OnMaterialYieldZChanged(double value) => OnMaterialEdited();
    partial void OnMaterialConservativeZChanged(bool value)
    {
        SyncMaterialToWorkflow();
        UpdateSafetyFactorUI();
    }

    private void OnMaterialEdited()
    {
        if (SelectedMaterialPreset != MaterialPresetKind.Custom)
        {
            SelectedMaterialPreset = MaterialPresetKind.Custom;
        }
        SyncMaterialToWorkflow();
    }

    private void SyncMaterialToWorkflow()
    {
        Workflow.State.Material.PresetKind = SelectedMaterialPreset;
        Workflow.State.Material.E = MaterialE;
        Workflow.State.Material.Nu = MaterialNu;
        Workflow.State.Material.YieldXy = MaterialYieldXy;
        Workflow.State.Material.YieldZ = MaterialYieldZ;
        Workflow.State.Material.ConservativeZ = MaterialConservativeZ;
        Workflow.State.NotifyMaterialChanged();
        QueueAutosave();
    }

    // Resolution Preset changes
    partial void OnSelectedResolutionPresetChanged(ResolutionPreset value)
    {
        if (value != ResolutionPreset.Custom)
        {
            TargetSolidElements = ResolutionConfig.GetPresetTargetElements(value);
        }
        RefreshResolutionMetrics();
        Workflow.SetResolution(new ResolutionConfig
        {
            Preset = value,
            TargetSolidElements = _targetSolidElements
        });
        QueueAutosave();
    }

    partial void OnTargetSolidElementsChanged(int value)
    {
        RefreshResolutionMetrics();
        Workflow.SetResolution(new ResolutionConfig
        {
            Preset = _selectedResolutionPreset,
            TargetSolidElements = value
        });
        QueueAutosave();
    }

    private void RefreshResolutionMetrics()
    {
        if (Workflow.State.Mesh != null)
        {
            float h = ResolutionConfig.ComputeVoxelSize(Workflow.State.Mesh, TargetSolidElements);
            VoxelSizeHText = $"h: {h:F2} mm";
        }
        else
        {
            VoxelSizeHText = "h: —";
        }

        EstimatedMemoryText = $"~{ResolutionConfig.EstimateMemoryMb(TargetSolidElements):F0} MB";
        EstimatedTimeText = $"~{ResolutionConfig.EstimateSolveTimeSeconds(TargetSolidElements):F1} s";
    }

    [RelayCommand]
    public async Task PrepareModelAsync()
    {
        if (Workflow.State.Mesh == null || IsPreparing) return;

        _prepareCts?.Cancel();
        _prepareCts = new CancellationTokenSource();
        IsPreparing = true;
        PrepareProgress = 0f;

        try
        {
            var prog = new Progress<double>(p => PrepareProgress = (float)(p * 100.0));
            var prepared = await Workflow.PrepareAsync(_prepareCts.Token, prog);

            ValidationIssues.Clear();
            foreach (var issue in prepared.ValidationIssues)
            {
                ValidationIssues.Add(issue);
            }

            NotifyStepBadges();
            RefreshBoundaryGlyphs();
        }
        catch (OperationCanceledException)
        {
            Trace.WriteLine("Prepare cancelled.");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Prepare error: {ex.Message}");
        }
        finally
        {
            IsPreparing = false;
        }
    }

    [RelayCommand]
    public void CancelPrepare()
    {
        _prepareCts?.Cancel();
    }

    [RelayCommand]
    public async Task RunStaticAnalysisAsync()
    {
        if (Workflow.State.PreparedModel == null || IsAnalyzing) return;

        _analysisCts?.Cancel();
        _analysisCts = new CancellationTokenSource();
        IsAnalyzing = true;
        AnalysisProgress = 0f;

        try
        {
            var prog = new Progress<double>(p => AnalysisProgress = (float)(p * 100.0));
            var result = await Workflow.RunStaticAnalysisAsync(_analysisCts.Token, prog);

            HasAnalysisResult = true;
            IsResultOutdated = false;

            ComplianceText = $"{result.Compliance:F1} mJ";
            MaxDisplacementText = $"{result.MaxDisplacement:F4} mm";
            MaxVonMisesText = $"{result.MaxVonMises:F1} MPa";
            P99VonMisesText = $"{result.P99VonMises:F1} MPa";

            UpdateSafetyFactorUI();
            RefreshFeaDisplay();
            NotifyStepBadges();
        }
        catch (OperationCanceledException)
        {
            Trace.WriteLine("Analysis cancelled.");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Analysis error: {ex.Message}");
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    [RelayCommand]
    public void CancelAnalysis()
    {
        _analysisCts?.Cancel();
    }

    [RelayCommand]
    public void FocusIssue(ValidationIssue? issue)
    {
        if (issue?.RelatedSelectionId == null) return;
        Guid id = issue.RelatedSelectionId.Value;

        var l = Loads.FirstOrDefault(x => x.Id == id);
        if (l != null)
        {
            ActiveStep = 2;
            ArmPicking(l);
            return;
        }

        var s = Supports.FirstOrDefault(x => x.Id == id);
        if (s != null)
        {
            ActiveStep = 3;
            ArmPicking(s);
        }
    }

    // FEA Result Toggle
    partial void OnIsVonMisesModeChanged(bool value) => RefreshFeaDisplay();
    partial void OnSelectedLutKindChanged(LutKind value) => RefreshFeaDisplay();

    public void RefreshFeaDisplay()
    {
        var res = Workflow.State.AnalysisResult;
        if (res == null || Workflow.State.Mesh == null)
        {
            FeaScalarFieldCleared?.Invoke();
            return;
        }

        if (IsVonMisesMode)
        {
            LegendUnitsText = "MPa";
            LegendMinText = $"{res.MinVonMises:F1}";
            LegendP99Text = $"p99: {res.P99VonMises:F1}";
            LegendMaxText = $"{res.MaxVonMises:F1}";
            FeaScalarFieldUpdated?.Invoke(res.VertexVonMises, SelectedLutKind, (float)res.MinVonMises, (float)res.P99VonMises);
        }
        else
        {
            LegendUnitsText = "mm";
            LegendMinText = $"{res.MinDisplacement:F4}";
            LegendP99Text = string.Empty;
            LegendMaxText = $"{res.MaxDisplacement:F4}";
            FeaScalarFieldUpdated?.Invoke(res.VertexDisplacements, SelectedLutKind, (float)res.MinDisplacement, (float)res.MaxDisplacement);
        }
    }

    private void UpdateSafetyFactorUI()
    {
        var res = Workflow.State.AnalysisResult;
        if (res == null) return;

        double sf = Workflow.State.Material.ComputeSafetyFactor(res.MaxVonMises);
        double sf99 = Workflow.State.Material.ComputeSafetyFactor(res.P99VonMises);

        SafetyFactorText = $"{sf:F2}";
        SafetyFactorP99Text = $"{sf99:F2}";

        SafetyFactorBrush = sf >= 3.0
            ? new SolidColorBrush(Color.FromRgb(16, 185, 129))  // Green
            : sf >= 2.0
                ? new SolidColorBrush(Color.FromRgb(245, 158, 11)) // Yellow
                : new SolidColorBrush(Color.FromRgb(239, 68, 68));   // Red
    }

    private void RefreshBoundaryGlyphs()
    {
        if (Workflow.State.Mesh == null)
        {
            GlyphsUpdated?.Invoke(null, 100f);
            return;
        }

        var (min, max) = Workflow.State.Mesh.BBox;
        float diag = (max - min).Length();

        var list = new List<Viewport3DControl.BoundaryGlyphData>();
        foreach (var l in Workflow.State.Loads)
        {
            if (l.Triangles.Length > 0)
            {
                list.Add(new Viewport3DControl.BoundaryGlyphData(
                    IsLoad: true,
                    Position: l.Centroid,
                    Direction: l.ComputeUnitDirection(),
                    Magnitude: l.MagnitudeNewtons));
            }
        }

        foreach (var s in Workflow.State.Supports)
        {
            if (s.Triangles.Length > 0)
            {
                list.Add(new Viewport3DControl.BoundaryGlyphData(
                    IsLoad: false,
                    Position: s.Centroid,
                    Direction: Vector3.UnitZ,
                    Magnitude: 0f));
            }
        }

        GlyphsUpdated?.Invoke(list, diag);
    }

    private void OnWorkflowStateChanged()
    {
        NotifyStepBadges();
    }

    private void OnPreparationInvalidated()
    {
        NotifyStepBadges();
    }

    private void OnAnalysisInvalidated()
    {
        IsResultOutdated = Workflow.State.AnalysisResult?.IsOutdated ?? false;
        NotifyStepBadges();
    }

    private void NotifyStepBadges()
    {
        OnPropertyChanged(nameof(IsStep1Completed));
        OnPropertyChanged(nameof(IsStep2Completed));
        OnPropertyChanged(nameof(IsStep3Completed));
        OnPropertyChanged(nameof(IsStep4Completed));
        OnPropertyChanged(nameof(IsStep5Completed));
        OnPropertyChanged(nameof(IsStep5Prepared));
    }

    private void UpdateModelMetricsUI()
    {
        var mesh = Workflow.State.Mesh;
        if (mesh == null)
        {
            StatusTriangles = "0 tris";
            StatusVertices = "0 verts";
            StatusBBox = "—";
            StatusVolume = "—";
            WatertightText = "No Mesh";
            WatertightBadgeBrush = new SolidColorBrush(Color.FromRgb(75, 85, 99));
            return;
        }

        StatusTriangles = $"{mesh.TriangleCount:N0} tris";
        StatusVertices = $"{mesh.VertexCount:N0} verts";

        var (min, max) = mesh.BBox;
        Vector3 size = max - min;
        StatusBBox = $"{size.X:F1} × {size.Y:F1} × {size.Z:F1} mm";
        StatusVolume = $"{Math.Abs(mesh.SignedVolume):N0} mm³";

        bool isWater = Workflow.State.MeshReport?.IsWatertight ?? false;
        WatertightText = isWater ? "Watertight" : "Non-Watertight";
        WatertightBadgeBrush = isWater
            ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
            : new SolidColorBrush(Color.FromRgb(245, 158, 11));
    }

    private void OnLoadItemChanged(LoadItemViewModel item)
    {
        if (Workflow.State.Mesh != null)
        {
            item.Model.UpdateGeometryMetrics(Workflow.State.Mesh);
        }
        SelectionOverlayUpdated?.Invoke(item.Id, item.Model.Triangles, item.ColorRgba);
        RefreshBoundaryGlyphs();
        Workflow.State.NotifySelectionChanged();
        QueueAutosave();
    }

    private void OnSupportItemChanged(SupportItemViewModel item)
    {
        if (Workflow.State.Mesh != null)
        {
            item.Model.UpdateGeometryMetrics(Workflow.State.Mesh);
        }
        SelectionOverlayUpdated?.Invoke(item.Id, item.Model.Triangles, item.ColorRgba);
        RefreshBoundaryGlyphs();
        Workflow.State.NotifySelectionChanged();
        QueueAutosave();
    }

    public void ModifyActiveSelectionTriangles(int[] grownTriangles, SelectionModifyMode mode)
    {
        if (!IsPickingActive || ActivePickingTarget == null) return;

        if (ActivePickingTarget is LoadItemViewModel lvm)
        {
            int[] updated = RegionSelector.ModifySelection(lvm.Model.Triangles, grownTriangles, mode);
            lvm.UpdateTriangles(updated);
            OnLoadItemChanged(lvm);
        }
        else if (ActivePickingTarget is SupportItemViewModel svm)
        {
            int[] updated = RegionSelector.ModifySelection(svm.Model.Triangles, grownTriangles, mode);
            svm.UpdateTriangles(updated);
            OnSupportItemChanged(svm);
        }
    }

    // Project Save / Open
    public async Task OpenProjectAsync(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath)) return;

        try
        {
            var state = await Task.Run(() => ProjectSerializer.Open(projectPath));
            Workflow.State.Clear();
            Workflow.State.Mesh = state.Mesh;
            Workflow.State.MeshReport = state.MeshReport;
            Workflow.State.FilePath = state.FilePath;
            Workflow.State.StlSha256 = state.StlSha256;
            Workflow.State.Unit = state.Unit;
            Workflow.State.Material = state.Material;
            Workflow.State.Resolution = state.Resolution;

            ModelFileName = Path.GetFileName(state.FilePath ?? "model.stl");
            CurrentProjectPath = Directory.Exists(projectPath) ? projectPath : Path.GetDirectoryName(projectPath)!;

            CurrentBvh = new Bvh(state.Mesh!);
            CurrentSelector = new RegionSelector(state.Mesh!);

            UpdateModelMetricsUI();

            Loads.Clear();
            foreach (var l in state.Loads)
            {
                Workflow.State.Loads.Add(l);
                var lvm = new LoadItemViewModel(l);
                lvm.Changed += OnLoadItemChanged;
                Loads.Add(lvm);
                SelectionOverlayUpdated?.Invoke(lvm.Id, lvm.Model.Triangles, lvm.ColorRgba);
            }

            Supports.Clear();
            foreach (var s in state.Supports)
            {
                Workflow.State.Supports.Add(s);
                var svm = new SupportItemViewModel(s);
                svm.Changed += OnSupportItemChanged;
                Supports.Add(svm);
                SelectionOverlayUpdated?.Invoke(svm.Id, svm.Model.Triangles, svm.ColorRgba);
            }

            SelectedMaterialPreset = state.Material.PresetKind;
            MaterialE = state.Material.E;
            MaterialNu = state.Material.Nu;
            MaterialYieldXy = state.Material.YieldXy;
            MaterialYieldZ = state.Material.YieldZ;
            MaterialConservativeZ = state.Material.ConservativeZ;

            SelectedResolutionPreset = state.Resolution.Preset;
            TargetSolidElements = state.Resolution.TargetSolidElements;

            OnPropertyChanged(nameof(SelectedMaterialPreset));
            OnPropertyChanged(nameof(MaterialE));
            OnPropertyChanged(nameof(MaterialNu));
            OnPropertyChanged(nameof(MaterialYieldXy));
            OnPropertyChanged(nameof(MaterialYieldZ));
            OnPropertyChanged(nameof(MaterialConservativeZ));
            OnPropertyChanged(nameof(SelectedResolutionPreset));
            OnPropertyChanged(nameof(TargetSolidElements));

            MeshLoaded?.Invoke(state.Mesh);
            RefreshBoundaryGlyphs();
            RefreshResolutionMetrics();
            NotifyStepBadges();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to open project: {ex.Message}");
        }
    }

    public async Task SaveProjectAsync(string? targetDir = null)
    {
        string dir = targetDir ?? CurrentProjectPath;
        if (string.IsNullOrWhiteSpace(dir)) return;

        try
        {
            await Task.Run(() => ProjectSerializer.Save(dir, Workflow.State));
            CurrentProjectPath = dir;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to save project: {ex.Message}");
        }
    }

    private void QueueAutosave()
    {
        if (!string.IsNullOrWhiteSpace(CurrentProjectPath))
        {
            _autosaveTimer.Stop();
            _autosaveTimer.Start();
        }
    }

    private async void OnAutosaveTick(object? sender, EventArgs e)
    {
        _autosaveTimer.Stop();
        if (!string.IsNullOrWhiteSpace(CurrentProjectPath))
        {
            await SaveProjectAsync(CurrentProjectPath);
        }
    }

    public void UpdateDiagnostics(double fps, string glSummary)
    {
        FpsText = $"{fps:F0} FPS";
    }
}
