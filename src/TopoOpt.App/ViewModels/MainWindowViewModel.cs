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
using TopoOpt.App.Diagnostics;
using TopoOpt.App.Rendering;
using TopoOpt.Core.Fem;
using TopoOpt.Core.Geometry;
using TopoOpt.Core.Optimization;
using TopoOpt.Core.Persistence;
using TopoOpt.Core.PostProcessing;
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
    private string _fpsText = "Idle (on-demand)";

    [ObservableProperty]
    private string _uiGapText = string.Empty;

    [ObservableProperty]
    private string _currentProjectPath = string.Empty;

    // Step 6: Optimisation Parameters & Live State
    private OptimizationCoordinator? _optCoordinator;
    private RunWriter? _activeRunWriter;
    private RunReader? _activeRunReader;
    private readonly PlaybackScrubber _playbackScrubber = new();
    private readonly DispatcherTimer _liveDisplayTimer;
    private readonly DispatcherTimer _playbackTimer;
    private readonly DispatcherTimer _diagnosticsTimer;
    private int _isLiveExtractionRunning;
    private readonly List<double> _complianceHistoryList = new();
    private double _avgIterMs = 150.0;

    public string[] AvailablePlaybackSpeeds { get; } = ["1×", "2×", "5×"];

    [ObservableProperty]
    private double _optVolumePercent = 30.0;

    [ObservableProperty]
    private double _optMinFeatureSizeMm = 1.2;

    [ObservableProperty]
    private int _optMaxIterations = 100;

    [ObservableProperty]
    private int _optKeepEveryN = 1;

    [ObservableProperty]
    private string _optEstimatedDiskText = "~4.5 MB";

    [ObservableProperty]
    private double _optPenal = 3.0;

    [ObservableProperty]
    private double _optMoveLimit = 0.2;

    [ObservableProperty]
    private bool _optEnableHeaviside;

    [ObservableProperty]
    private double _optEminRatio = 1e-9;

    [ObservableProperty]
    private bool _isOptimising;

    [ObservableProperty]
    private bool _isOptimisationPaused;

    [ObservableProperty]
    private bool _canContinueInterruptedRun;

    [ObservableProperty]
    private string _optLiveIterationText = "0 / 100";

    [ObservableProperty]
    private string _optLiveComplianceText = "—";

    [ObservableProperty]
    private string _optLiveVolumeText = "—";

    [ObservableProperty]
    private string _optLiveChangeText = "—";

    [ObservableProperty]
    private string _optLiveGreyText = "—";

    [ObservableProperty]
    private string _optLiveMsPerIterText = "—";

    [ObservableProperty]
    private string _optLiveEtaText = "—";

    [ObservableProperty]
    private string _optLiveSolverItersText = "—";

    [ObservableProperty]
    private string _optLiveDiskUsedText = "0.0 MB";

    [ObservableProperty]
    private IReadOnlyList<double> _complianceHistory = Array.Empty<double>();

    // Timeline & Playback State
    [ObservableProperty]
    private bool _hasLoadedRun;

    [ObservableProperty]
    private int _selectedIteration = 1;

    [ObservableProperty]
    private int _maxAvailableIteration = 1;

    [ObservableProperty]
    private string _timelineIterationLabel = "iteration 0 / 0";

    [ObservableProperty]
    private bool _isPlayingTimeline;

    [ObservableProperty]
    private string _selectedPlaybackSpeed = "1×";

    [ObservableProperty]
    private bool _isLiveFollow = true;

    [ObservableProperty]
    private bool _showGhostOriginal = true;

    private bool _suppressScrubOnSliderChange;

    // Verify & Export State
    [ObservableProperty]
    private float _verifyThreshold = 0.5f;

    [ObservableProperty]
    private int _exportSmoothIterations = 3;

    [ObservableProperty]
    private bool _isVerifyingDesign;

    [ObservableProperty]
    private bool _hasVerificationResult;

    [ObservableProperty]
    private string _verifyMaterialSavedText = "—";

    [ObservableProperty]
    private string _verifyComplianceRatioText = "—";

    [ObservableProperty]
    private string _exportStlStatusText = string.Empty;

    [ObservableProperty]
    private bool _isAboutDialogOpen;

    [RelayCommand]
    private void OpenAboutDialog() => IsAboutDialogOpen = true;

    [RelayCommand]
    private void CloseAboutDialog() => IsAboutDialogOpen = false;

    public void HandleGlFailure(string error)
    {
        ValidationIssues.Add(new ValidationIssue("GL_INIT_ERROR", ValidationSeverity.Error, error));
        NotifyStepBadges();
    }

    // Events for Viewport
    public event Action<IndexedMesh?>? MeshLoaded;
    public event Action<IndexedMesh?, bool>? GhostOriginalMeshUpdated;
    public event Action<bool>? ShowGhostOriginalChanged;
    public event Func<IndexedMesh, Task>? AsyncIsoMeshUpdated;
    public event Action? ComplianceChartUpdated;
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

        _liveDisplayTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(125) // ~8 Hz UI display slot polling
        };
        _liveDisplayTimer.Tick += OnLiveDisplayTimerTick;

        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200) // 1x = 5 fps
        };
        _playbackTimer.Tick += OnPlaybackTimerTick;

        _diagnosticsTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _diagnosticsTimer.Tick += OnDiagnosticsTimerTick;
        _diagnosticsTimer.Start();

        RefreshOptimisationDiskEstimate();
    }

    private void OnDiagnosticsTimerTick(object? sender, EventArgs e)
    {
        var (_, statusText) = RenderLoopProbe.Instance.GetStatus();
        FpsText = statusText;
        double maxGap = UiResponsivenessMonitor.Instance.MaxGapMs;
        UiGapText = maxGap > 0 ? $"Max UI gap: {maxGap:F0} ms" : "";
    }

    // Step Completion Badges
    public bool IsStep1Completed => Workflow.State.Mesh != null;
    public bool IsStep2Completed => Workflow.State.Loads.Count > 0;
    public bool IsStep3Completed => Workflow.State.Supports.Count > 0;
    public bool IsStep4Completed => Workflow.State.Material != null;
    public bool IsStep5Completed => Workflow.State.AnalysisResult != null && !Workflow.State.AnalysisResult.IsOutdated;
    public bool IsStep5Prepared => Workflow.State.PreparedModel != null && !Workflow.State.PreparedModel.IsOutdated;
    public bool IsStep6Completed => HasLoadedRun && !IsOptimising;
    public bool HasValidationErrors => Workflow.State.PreparedModel?.ValidationIssues.Any(i => i.Severity == ValidationSeverity.Error) ?? false;
    public bool CanStartOptimisation => IsStep5Prepared && !HasValidationErrors && !IsOptimising;
    public string StartOptimisationBlockingReason
    {
        get
        {
            if (IsOptimising) return string.Empty;
            if (!IsStep5Prepared) return "Prepare the voxel model in Step 5 before starting optimisation.";
            if (HasValidationErrors) return "Resolve validation errors in Step 5 before starting optimisation.";
            return string.Empty;
        }
    }
    public bool HasStartOptimisationBlockingReason => !string.IsNullOrEmpty(StartOptimisationBlockingReason);

    public bool IsStep1Active => ActiveStep == 1;
    public bool IsStep2Active => ActiveStep == 2;
    public bool IsStep3Active => ActiveStep == 3;
    public bool IsStep4Active => ActiveStep == 4;
    public bool IsStep5Active => ActiveStep == 5;
    public bool IsStep6Active => ActiveStep == 6;

    partial void OnActiveStepChanged(int value)
    {
        if (IsPickingActive)
        {
            DisarmPicking();
        }

        OnPropertyChanged(nameof(IsStep1Active));
        OnPropertyChanged(nameof(IsStep2Active));
        OnPropertyChanged(nameof(IsStep3Active));
        OnPropertyChanged(nameof(IsStep4Active));
        OnPropertyChanged(nameof(IsStep5Active));
        OnPropertyChanged(nameof(IsStep6Active));
    }

    public void SelectStep(int step)
    {
        if (IsPickingActive)
        {
            DisarmPicking();
        }
        ActiveStep = step;
    }

    /// <summary>
    /// Command target for the stepper headers. XAML <c>CommandParameter="N"</c> is a string, so a
    /// typed int command would be silently disabled (CanExecute false); parse it here instead.
    /// </summary>
    [RelayCommand]
    private void SelectStepFromHeader(string? step)
    {
        if (IsPickingActive)
        {
            DisarmPicking();
        }

        if (int.TryParse(step, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n) && n is >= 1 and <= 6)
        {
            SelectStep(n);
        }
    }

    public async Task LoadStlFileAsync(string filePath, LengthUnit unit = LengthUnit.Millimeter)
    {
        if (IsPickingActive) DisarmPicking();
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
            GhostOriginalMeshUpdated?.Invoke(mesh, false);
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
        GhostOriginalMeshUpdated?.Invoke(mesh, false);
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
        GhostOriginalMeshUpdated?.Invoke(Workflow.State.Mesh, false);
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
        GhostOriginalMeshUpdated?.Invoke(Workflow.State.Mesh, false);
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
        foreach (var l in Loads) l.IsPicking = false;
        foreach (var s in Supports) s.IsPicking = false;
        foreach (var p in Preserves) p.IsPicking = false;

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
        RefreshOptimisationDiskEstimate();
    }

    [RelayCommand]
    public async Task PrepareModelAsync()
    {
        if (IsPickingActive) DisarmPicking();
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
        if (IsPickingActive) DisarmPicking();
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
            Console.WriteLine($"[Analysis error] {ex}");
            try { File.AppendAllText(Path.Combine(CrashLogger.LogDir, "scenario.log"), $"[{DateTime.UtcNow:O}] [Analysis error] {ex}\n"); } catch { }
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
        OnPropertyChanged(nameof(IsStep6Completed));
        OnPropertyChanged(nameof(HasValidationErrors));
        OnPropertyChanged(nameof(CanStartOptimisation));
        OnPropertyChanged(nameof(StartOptimisationBlockingReason));
        OnPropertyChanged(nameof(HasStartOptimisationBlockingReason));
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
        if (IsPickingActive) DisarmPicking();
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
            GhostOriginalMeshUpdated?.Invoke(state.Mesh, false);
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
        if (IsPickingActive) DisarmPicking();
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

    // -------------------------------------------------------------------------
    // Step 6: Live Topology Optimisation, Run Storage, Playback, Verify & Export
    // -------------------------------------------------------------------------

    partial void OnOptMaxIterationsChanged(int value) => RefreshOptimisationDiskEstimate();
    partial void OnOptKeepEveryNChanged(int value) => RefreshOptimisationDiskEstimate();

    partial void OnShowGhostOriginalChanged(bool value)
    {
        ShowGhostOriginalChanged?.Invoke(value);
    }

    partial void OnSelectedPlaybackSpeedChanged(string value)
    {
        double fps = value switch
        {
            "2×" => 10.0,
            "5×" => 25.0,
            _ => 5.0
        };
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / fps);
    }

    partial void OnSelectedIterationChanged(int value)
    {
        TimelineIterationLabel = $"iteration {value} / {MaxAvailableIteration}";
        ComplianceChartUpdated?.Invoke();

        if (_suppressScrubOnSliderChange)
            return;

        if (IsOptimising && value < MaxAvailableIteration)
        {
            IsLiveFollow = false;
        }

        _ = ScrubToIterationAsync(value);
    }

    private void RefreshOptimisationDiskEstimate()
    {
        int totalElements = Workflow.State.PreparedModel != null
            ? Workflow.State.PreparedModel.FeModel.TotalElements
            : Math.Max(10_000, TargetSolidElements * 2);

        int keepEvery = Math.Max(1, OptKeepEveryN);
        int savedFrames = Math.Max(1, OptMaxIterations / keepEvery);

        // Brotli-compressed 1-byte physical density is typically ~0.35 bytes/voxel + 72B header
        double bytesPerIter = 72.0 + totalElements * 0.35;
        double totalMb = (bytesPerIter * savedFrames + 65536.0) / (1024.0 * 1024.0);
        double kbPerIter = bytesPerIter / 1024.0;
        OptEstimatedDiskText = $"~{kbPerIter:F0} KB/it → ~{totalMb:F1} MB";
    }

    private OptimizerOptions BuildOptimizerOptions(int startIteration = 1, float[]? initialDensity = null)
    {
        return new OptimizerOptions
        {
            VolumeFraction = Math.Clamp(OptVolumePercent / 100.0, 0.05, 0.95),
            FilterRadiusMm = Math.Max(0.1, OptMinFeatureSizeMm),
            StartIteration = startIteration,
            MaxIterations = Math.Max(startIteration, OptMaxIterations),
            Penal = Math.Clamp(OptPenal, 1.0, 5.0),
            MoveLimit = Math.Clamp(OptMoveLimit, 0.02, 0.5),
            EnableHeaviside = OptEnableHeaviside,
            EminRatio = Math.Clamp(OptEminRatio, 1e-12, 1e-3),
            PassiveMask = Workflow.State.PreparedModel?.PassiveShell,
            InitialDensity = initialDensity
        };
    }

    [RelayCommand]
    public async Task StartOptimisationAsync()
    {
        if (IsPickingActive) DisarmPicking();
        if (!CanStartOptimisation || Workflow.State.PreparedModel == null)
            return;

        await ExecuteOptimisationRunAsync(isResume: false);
    }

    [RelayCommand]
    public async Task ContinueInterruptedRunAsync()
    {
        if (!CanContinueInterruptedRun || _activeRunReader == null || Workflow.State.PreparedModel == null)
            return;

        await ExecuteOptimisationRunAsync(isResume: true);
    }

    private async Task ExecuteOptimisationRunAsync(bool isResume)
    {
        var prep = Workflow.State.PreparedModel!;
        var baseFe = prep.FeModel;
        var mat = new FeMaterial(Workflow.State.Material.E, Workflow.State.Material.Nu, Math.Clamp(OptEminRatio, 1e-12, 1e-3));
        var feModel = new FeModel(baseFe.Grid, baseFe.Domain, mat, baseFe.FixedDofs, baseFe.Loads);

        string runFolder;
        OptimizerOptions opts;
        RunMetadata meta;

        if (isResume && _activeRunReader != null)
        {
            runFolder = _activeRunReader.RunFolder;
            var ckpt = _activeRunReader.ReadLatestCheckpoint();
            int[] avail = _activeRunReader.GetAvailableIterations();
            int lastIt = ckpt?.Iteration ?? (avail.Length > 0 ? avail[^1] : 0);
            int startIt = lastIt + 1;
            opts = BuildOptimizerOptions(startIt, ckpt?.DesignDensity);
            meta = _activeRunReader.Metadata;
            meta.Status = RunStatus.Running;
        }
        else
        {
            string baseRunsDir = !string.IsNullOrWhiteSpace(CurrentProjectPath)
                ? Path.Combine(CurrentProjectPath, "runs")
                : Path.Combine(Environment.CurrentDirectory, "runs");
            Directory.CreateDirectory(baseRunsDir);

            string safeModelName = Path.GetFileNameWithoutExtension(ModelFileName);
            if (string.IsNullOrWhiteSpace(safeModelName) || safeModelName == "No File Loaded")
                safeModelName = "model";
            foreach (char c in Path.GetInvalidFileNameChars())
                safeModelName = safeModelName.Replace(c, '_');

            string folderName = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{safeModelName}";
            runFolder = Path.Combine(baseRunsDir, folderName);

            opts = BuildOptimizerOptions(1, null);
            meta = Workflow.CreateRunMetadata(opts, Math.Max(1, OptKeepEveryN));

            _complianceHistoryList.Clear();
            ComplianceHistory = Array.Empty<double>();
            ComplianceChartUpdated?.Invoke();
        }

        byte[] passiveMask = prep.PassiveShell ?? new byte[baseFe.TotalElements];

        _optCoordinator?.Dispose();
        _optCoordinator = new OptimizationCoordinator();
        _activeRunWriter?.Dispose();
        _activeRunWriter = new RunWriter(runFolder, meta, baseFe.Domain, passiveMask, isResume);
        _activeRunReader = RunReader.Open(runFolder, isLiveActiveRun: true);

        IsOptimising = true;
        IsOptimisationPaused = false;
        CanContinueInterruptedRun = false;
        HasLoadedRun = true;
        IsLiveFollow = true;
        HasVerificationResult = false;
        FeaScalarFieldCleared?.Invoke();
        AllOverlaysCleared?.Invoke();

        if (Workflow.State.Mesh != null)
        {
            GhostOriginalMeshUpdated?.Invoke(Workflow.State.Mesh, ShowGhostOriginal);
        }

        ActiveStep = 6;
        NotifyStepBadges();
        _liveDisplayTimer.Start();

        try
        {
            await _optCoordinator.StartRunAsync(feModel, opts, _activeRunWriter);
        }
        catch (OperationCanceledException)
        {
            Trace.WriteLine("Optimisation run stopped.");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Optimisation run failed: {ex.Message}");
        }
        finally
        {
            _liveDisplayTimer.Stop();

            // Drain any remaining snapshot in the display slot
            await PollDisplaySlotOnceAsync();

            if (_activeRunReader != null)
            {
                int[] avail = _activeRunReader.GetAvailableIterations();
                if (avail.Length > 0)
                {
                    MaxAvailableIteration = avail[^1];
                    _suppressScrubOnSliderChange = true;
                    SelectedIteration = MaxAvailableIteration;
                    _suppressScrubOnSliderChange = false;
                    TimelineIterationLabel = $"iteration {SelectedIteration} / {MaxAvailableIteration}";
                }
            }

            IsOptimising = false;
            IsOptimisationPaused = false;
            NotifyStepBadges();
        }
    }

    [RelayCommand]
    public void PauseOptimisation()
    {
        if (!IsOptimising || IsOptimisationPaused) return;
        _optCoordinator?.Pause();
        IsOptimisationPaused = true;
    }

    [RelayCommand]
    public void ResumeOptimisation()
    {
        if (!IsOptimising || !IsOptimisationPaused) return;
        _optCoordinator?.Resume();
        IsOptimisationPaused = false;
    }

    [RelayCommand]
    public async Task StopOptimisationAsync()
    {
        if (!IsOptimising || _optCoordinator == null) return;
        await _optCoordinator.StopAndFinalizeAsync();
    }

    public async Task StopAndFinalizeActiveRunAsync()
    {
        if (_optCoordinator != null && IsOptimising)
        {
            await _optCoordinator.StopAndFinalizeAsync();
        }
    }

    private async void OnLiveDisplayTimerTick(object? sender, EventArgs e)
    {
        await PollDisplaySlotOnceAsync();
    }

    private async Task PollDisplaySlotOnceAsync()
    {
        if (_optCoordinator == null || Workflow.State.PreparedModel == null)
            return;

        if (Interlocked.CompareExchange(ref _isLiveExtractionRunning, 1, 0) != 0)
            return;

        try
        {
            IterationSnapshot? snap = _optCoordinator.DisplaySlot.TryTakeLatest();
            if (snap == null)
                return;

            UpdateLiveMetricsFromSnapshot(snap);

            if (IsLiveFollow)
            {
                var grid = Workflow.State.PreparedModel.FeModel.Grid;
                float threshold = VerifyThreshold;
                float[] rhoPhys = snap.PhysicalDensity;

                IndexedMesh isoMesh = await Task.Run(() =>
                    SurfaceNetsExtractor.Extract(grid, rhoPhys, threshold));

                if (AsyncIsoMeshUpdated != null)
                {
                    await AsyncIsoMeshUpdated.Invoke(isoMesh);
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Live display extraction error: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _isLiveExtractionRunning, 0);
        }
    }

    private void UpdateLiveMetricsFromSnapshot(IterationSnapshot snap)
    {
        if (_complianceHistoryList.Count < snap.Iteration - 1 && _activeRunReader != null)
        {
            var metrics = _activeRunReader.ReadMetrics();
            while (_complianceHistoryList.Count < snap.Iteration - 1)
            {
                int targetIt = _complianceHistoryList.Count + 1;
                var m = metrics.FirstOrDefault(r => r.Iteration == targetIt);
                _complianceHistoryList.Add(m != null && m.Compliance > 0 ? m.Compliance : snap.Compliance);
            }
        }
        if (_complianceHistoryList.Count == snap.Iteration - 1)
        {
            _complianceHistoryList.Add(snap.Compliance);
        }
        else if (snap.Iteration - 1 >= 0 && snap.Iteration - 1 < _complianceHistoryList.Count)
        {
            _complianceHistoryList[snap.Iteration - 1] = snap.Compliance;
        }

        ComplianceHistory = _complianceHistoryList.ToArray();

        _avgIterMs = _avgIterMs * 0.7 + snap.TotalMs * 0.3;
        int remIters = Math.Max(0, OptMaxIterations - snap.Iteration);
        double etaSec = (remIters * _avgIterMs) / 1000.0;

        OptLiveIterationText = $"{snap.Iteration} / {OptMaxIterations}";
        OptLiveComplianceText = $"{snap.Compliance:G5} mJ";
        OptLiveVolumeText = $"{snap.VolumeFraction * 100.0:F1} %";
        OptLiveChangeText = $"{snap.MaxChange:F4}";
        OptLiveGreyText = $"{snap.GreyLevel * 100.0:F1} %";
        OptLiveMsPerIterText = $"{snap.TotalMs:F0} ms";
        OptLiveEtaText = etaSec >= 60.0 ? $"{etaSec / 60.0:F1} min" : $"{etaSec:F1} s";
        OptLiveSolverItersText = $"{snap.SolverIterations}";

        long diskBytes = _activeRunWriter?.RunningDiskBytes ?? ComputeFolderBytes(_activeRunReader?.RunFolder);
        OptLiveDiskUsedText = $"{diskBytes / (1024.0 * 1024.0):F2} MB";

        MaxAvailableIteration = Math.Max(MaxAvailableIteration, snap.Iteration);
        if (IsLiveFollow)
        {
            _suppressScrubOnSliderChange = true;
            SelectedIteration = snap.Iteration;
            _suppressScrubOnSliderChange = false;
            TimelineIterationLabel = $"iteration {SelectedIteration} / {MaxAvailableIteration}";
        }

        ComplianceChartUpdated?.Invoke();
    }

    private static long ComputeFolderBytes(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return 0L;

        try
        {
            long sum = 0L;
            foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                sum += new FileInfo(file).Length;
            }
            return sum;
        }
        catch
        {
            return 0L;
        }
    }

    public async Task OpenRunFolderAsync(string runFolder)
    {
        if (string.IsNullOrWhiteSpace(runFolder) || !Directory.Exists(runFolder))
            return;

        try
        {
            var reader = await Task.Run(() => RunReader.Open(runFolder, isLiveActiveRun: false));
            _activeRunReader = reader;

            var metrics = reader.ReadMetrics();
            _complianceHistoryList.Clear();
            foreach (var row in metrics)
            {
                _complianceHistoryList.Add(row.Compliance);
            }
            ComplianceHistory = _complianceHistoryList.ToArray();

            int[] avail = reader.GetAvailableIterations();
            HasLoadedRun = avail.Length > 0;
            CanContinueInterruptedRun = reader.Metadata.Status == RunStatus.Interrupted && Workflow.State.PreparedModel != null;
            MaxAvailableIteration = avail.Length > 0 ? avail[^1] : 1;
            OptLiveDiskUsedText = $"{ComputeFolderBytes(reader.RunFolder) / (1024.0 * 1024.0):F2} MB";

            if (metrics.Count > 0)
            {
                var last = metrics[^1];
                OptLiveIterationText = $"{last.Iteration} / {reader.Metadata.Options.MaxIterations}";
                OptLiveComplianceText = $"{last.Compliance:G5} mJ";
                OptLiveVolumeText = $"{last.VolumeFraction * 100.0:F1} %";
                OptLiveChangeText = $"{last.MaxChange:F4}";
                OptLiveGreyText = $"{last.GreyLevel * 100.0:F1} %";
                OptLiveMsPerIterText = $"{last.TotalMs:F0} ms";
                OptLiveSolverItersText = $"{last.SolverIterations}";
                OptLiveEtaText = reader.Metadata.Status == RunStatus.Interrupted ? "Interrupted" : "Done";
            }

            if (Workflow.State.Mesh != null)
            {
                GhostOriginalMeshUpdated?.Invoke(Workflow.State.Mesh, ShowGhostOriginal);
            }

            ActiveStep = 6;
            SelectedIteration = MaxAvailableIteration;
            await ScrubToIterationAsync(SelectedIteration);
            NotifyStepBadges();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to open run folder: {ex.Message}");
        }
    }

    public async Task ScrubToIterationAsync(int targetIteration)
    {
        if (_activeRunReader == null) return;

        int[] avail = _activeRunReader.GetAvailableIterations();
        if (avail.Length == 0) return;

        // Snap to nearest available saved iteration
        int snapped = avail[0];
        int minDiff = Math.Abs(targetIteration - snapped);
        for (int i = 1; i < avail.Length; i++)
        {
            int diff = Math.Abs(targetIteration - avail[i]);
            if (diff < minDiff)
            {
                minDiff = diff;
                snapped = avail[i];
            }
        }

        var metrics = _activeRunReader.ReadMetrics();
        var row = metrics.FirstOrDefault(m => m.Iteration == snapped);
        if (row != null)
        {
            OptLiveIterationText = $"{row.Iteration} / {_activeRunReader.Metadata.Options.MaxIterations}";
            OptLiveComplianceText = $"{row.Compliance:G5} mJ";
            OptLiveVolumeText = $"{row.VolumeFraction * 100.0:F1} %";
            OptLiveChangeText = $"{row.MaxChange:F4}";
            OptLiveGreyText = $"{row.GreyLevel * 100.0:F1} %";
            OptLiveMsPerIterText = $"{row.TotalMs:F0} ms";
            OptLiveSolverItersText = $"{row.SolverIterations}";
        }

        float threshold = VerifyThreshold;
        var reader = _activeRunReader;

        await _playbackScrubber.SeekIterationAsync(
            snapped,
            (it, ct) => Task.FromResult(reader.GetOrExtractMesh(it, threshold, ct)),
            async (_, mesh) =>
            {
                if (AsyncIsoMeshUpdated != null)
                {
                    await AsyncIsoMeshUpdated.Invoke(mesh);
                }
            });
    }

    public void ScrubFromChart(int iteration)
    {
        if (!HasLoadedRun) return;
        IsPlayingTimeline = false;
        _playbackTimer.Stop();
        if (IsOptimising) IsLiveFollow = false;
        SelectedIteration = Math.Clamp(iteration, 1, MaxAvailableIteration);
    }

    [RelayCommand]
    public void TogglePlayTimeline()
    {
        if (!HasLoadedRun) return;

        if (IsPlayingTimeline)
        {
            IsPlayingTimeline = false;
            _playbackTimer.Stop();
        }
        else
        {
            IsLiveFollow = false;
            if (SelectedIteration >= MaxAvailableIteration)
            {
                SelectedIteration = 1;
            }
            IsPlayingTimeline = true;
            _playbackTimer.Start();
        }
    }

    [RelayCommand]
    public void StepBackwardTimeline()
    {
        if (!HasLoadedRun) return;
        IsPlayingTimeline = false;
        _playbackTimer.Stop();
        IsLiveFollow = false;
        SelectedIteration = Math.Max(1, SelectedIteration - 1);
    }

    [RelayCommand]
    public void StepForwardTimeline()
    {
        if (!HasLoadedRun) return;
        IsPlayingTimeline = false;
        _playbackTimer.Stop();
        SelectedIteration = Math.Min(MaxAvailableIteration, SelectedIteration + 1);
    }

    private void OnPlaybackTimerTick(object? sender, EventArgs e)
    {
        if (!IsPlayingTimeline || !HasLoadedRun)
        {
            _playbackTimer.Stop();
            IsPlayingTimeline = false;
            return;
        }

        if (SelectedIteration < MaxAvailableIteration)
        {
            SelectedIteration++;
        }
        else
        {
            IsPlayingTimeline = false;
            _playbackTimer.Stop();
        }
    }

    [RelayCommand]
    public async Task VerifyDesignAsync()
    {
        if (_activeRunReader == null || IsVerifyingDesign)
            return;

        IsVerifyingDesign = true;
        try
        {
            int it = SelectedIteration;
            float threshold = VerifyThreshold;
            float[] rhoPhys = await Task.Run(() => _activeRunReader.ReadIterationDensity(it));
            var verResult = await Workflow.VerifyIterationAsync(it, rhoPhys, threshold);

            if (AsyncIsoMeshUpdated != null)
            {
                await AsyncIsoMeshUpdated.Invoke(verResult.IsoMesh);
            }

            HasVerificationResult = true;
            HasAnalysisResult = true;
            IsResultOutdated = false;

            ComplianceText = $"{verResult.Compliance:F1} mJ";
            MaxDisplacementText = $"{verResult.MaxDisplacement:F4} mm";
            MaxVonMisesText = $"{verResult.MaxVonMises:F1} MPa";
            P99VonMisesText = $"{verResult.P99VonMises:F1} MPa";
            SafetyFactorText = $"{verResult.SafetyFactorMax:F2}";
            SafetyFactorP99Text = $"{verResult.SafetyFactorP99:F2}";
            VerifyMaterialSavedText = $"{verResult.MaterialSavedPercent:F1} %";
            VerifyComplianceRatioText = $"{verResult.ComplianceRatio:F2}×";

            SafetyFactorBrush = verResult.SafetyFactorMax >= 3.0
                ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
                : verResult.SafetyFactorMax >= 2.0
                    ? new SolidColorBrush(Color.FromRgb(245, 158, 11))
                    : new SolidColorBrush(Color.FromRgb(239, 68, 68));

            LegendUnitsText = "MPa";
            LegendMinText = "0.0";
            LegendP99Text = $"p99: {verResult.P99VonMises:F1}";
            LegendMaxText = $"{verResult.MaxVonMises:F1}";
            FeaScalarFieldUpdated?.Invoke(verResult.VertexVonMises, SelectedLutKind, 0f, (float)verResult.P99VonMises);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Verify design failed: {ex.Message}");
        }
        finally
        {
            IsVerifyingDesign = false;
        }
    }

    public async Task ExportCurrentIterationStlAsync(string outputStlPath)
    {
        if (_activeRunReader == null || string.IsNullOrWhiteSpace(outputStlPath))
            return;

        try
        {
            int it = SelectedIteration;
            float threshold = VerifyThreshold;
            int smooth = Math.Clamp(ExportSmoothIterations, 0, 10);
            var grid = _activeRunReader.Grid;
            float[] rhoPhys = await Task.Run(() => _activeRunReader.ReadIterationDensity(it));

            var (_, reReadMesh, report, vol) = await WorkflowService.ExportIterationStlAsync(
                outputStlPath,
                grid,
                it,
                rhoPhys,
                threshold,
                smooth);

            string wt = report.IsWatertight ? "Watertight" : "Non-Watertight";
            ExportStlStatusText = $"Exported {Path.GetFileName(outputStlPath)}: {wt}, {reReadMesh.TriangleCount:N0} tris, Vol = {vol:N0} mm³";
        }
        catch (Exception ex)
        {
            ExportStlStatusText = $"Export failed: {ex.Message}";
            Trace.WriteLine($"Export STL failed: {ex.Message}");
        }
    }
}
