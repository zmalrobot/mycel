using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Mycel.Core.Workflow;

namespace Mycel.App.ViewModels;

public partial class PreserveItemViewModel : ObservableObject
{
    public PreserveRegion Model { get; }
    public Guid Id => Model.Id;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private int _triangleCount;

    [ObservableProperty]
    private float _areaMm2;

    [ObservableProperty]
    private string _summaryText = "0 triangles · 0.0 mm²";

    [ObservableProperty]
    private bool _isHighCoverage;

    [ObservableProperty]
    private string _highCoverageTooltip = string.Empty;

    [ObservableProperty]
    private bool _isDisconnected;

    [ObservableProperty]
    private string _disconnectedTooltip = string.Empty;

    [ObservableProperty]
    private bool _isHovered;

    [ObservableProperty]
    private bool _isPicking;

    public IBrush ColorBrush { get; } = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // #10B981 Green
    public uint ColorRgba => 0x10B981FFu;

    public event Action<PreserveItemViewModel>? Changed;

    public PreserveItemViewModel(PreserveRegion model)
    {
        Model = model;
        _name = model.Name;
        _triangleCount = model.Triangles.Length;
    }

    partial void OnNameChanged(string value)
    {
        Model.Name = value;
        Changed?.Invoke(this);
    }

    public void UpdateTriangles(int[] triangles)
    {
        Model.Triangles = triangles;
        TriangleCount = triangles.Length;
        Changed?.Invoke(this);
    }

    public void RefreshMetrics()
    {
        AreaMm2 = Model.SurfaceAreaMm2;
        SummaryText = $"{TriangleCount} triangles · {AreaMm2:N1} mm²";

        IsHighCoverage = Model.IsHighCoverage;
        HighCoverageTooltip = $"Selection covers {Model.ModelSurfaceCoveragePercent:F1}% of the entire surface area (> 40%).";

        IsDisconnected = Model.IsDisconnected;
        DisconnectedTooltip = $"Selection contains {Model.ConnectedComponentCount} separate disconnected pieces.";
    }
}

