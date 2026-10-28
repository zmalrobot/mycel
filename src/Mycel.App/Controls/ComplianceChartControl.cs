using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Mycel.App.Controls;

/// <summary>
/// Custom Avalonia control rendering a live Compliance-vs-Iteration line chart
/// with a vertical playback cursor and click/drag scrubbing.
/// Uses pure Avalonia DrawingContext with zero external chart libraries.
/// </summary>
public sealed class ComplianceChartControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ComplianceHistoryProperty =
        AvaloniaProperty.Register<ComplianceChartControl, IReadOnlyList<double>?>(nameof(ComplianceHistory));

    public static readonly StyledProperty<int> SelectedIterationProperty =
        AvaloniaProperty.Register<ComplianceChartControl, int>(nameof(SelectedIteration), 0);

    public static readonly StyledProperty<int> MaxIterationsProperty =
        AvaloniaProperty.Register<ComplianceChartControl, int>(nameof(MaxIterations), 100);

    public IReadOnlyList<double>? ComplianceHistory
    {
        get => GetValue(ComplianceHistoryProperty);
        set => SetValue(ComplianceHistoryProperty, value);
    }

    public int SelectedIteration
    {
        get => GetValue(SelectedIterationProperty);
        set => SetValue(SelectedIterationProperty, value);
    }

    public int MaxIterations
    {
        get => GetValue(MaxIterationsProperty);
        set => SetValue(MaxIterationsProperty, value);
    }

    public event Action<int>? IterationScrubbed;

    private bool _isDragging;

    private static readonly IBrush BgBrush = new SolidColorBrush(Color.Parse("#0B0F17"));
    private static readonly IPen BorderPen = new Pen(new SolidColorBrush(Color.Parse("#1F2937")), 1.0);
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.Parse("#182232")), 1.0);
    private static readonly IPen CurvePen = new Pen(new SolidColorBrush(Color.Parse("#38BDF8")), 1.8);
    private static readonly IBrush AreaFillBrush = new SolidColorBrush(Color.Parse("#2238BDF8"));
    private static readonly IPen CursorPen = new Pen(new SolidColorBrush(Color.Parse("#F59E0B")), 1.5);
    private static readonly IBrush CursorDotBrush = new SolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#9CA3AF"));

    static ComplianceChartControl()
    {
        AffectsRender<ComplianceChartControl>(
            ComplianceHistoryProperty,
            SelectedIterationProperty,
            MaxIterationsProperty);
    }

    public void NotifyDataUpdated()
    {
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w <= 8 || h <= 8) return;

        var rect = new Rect(0, 0, w, h);
        context.DrawRectangle(BgBrush, BorderPen, rect, 6.0, 6.0);

        double padLeft = 8.0;
        double padRight = 8.0;
        double padTop = 16.0;
        double padBottom = 16.0;
        double plotW = Math.Max(1.0, w - padLeft - padRight);
        double plotH = Math.Max(1.0, h - padTop - padBottom);

        // Draw horizontal grid lines
        for (int i = 1; i <= 3; i++)
        {
            double gy = padTop + plotH * (i / 4.0);
            context.DrawLine(GridPen, new Point(padLeft, gy), new Point(w - padRight, gy));
        }

        var data = ComplianceHistory;
        if (data is null || data.Count == 0)
        {
            var emptyText = new FormattedText(
                "Compliance vs. Iteration",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                10.5,
                LabelBrush);
            context.DrawText(emptyText, new Point(
                Math.Max(4, (w - emptyText.Width) * 0.5),
                Math.Max(4, (h - emptyText.Height) * 0.5)));
            return;
        }

        int count = data.Count;
        double minC = double.PositiveInfinity;
        double maxC = double.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            double v = data[i];
            if (double.IsFinite(v))
            {
                if (v < minC) minC = v;
                if (v > maxC) maxC = v;
            }
        }

        if (!double.IsFinite(minC) || !double.IsFinite(maxC))
            return;

        if (Math.Abs(maxC - minC) < 1e-12)
        {
            maxC = minC + 1.0;
        }

        int xSpan = Math.Max(count - 1, 1);

        var lineGeo = new StreamGeometry();
        var fillGeo = new StreamGeometry();

        using (var lineCtx = lineGeo.Open())
        using (var fillCtx = fillGeo.Open())
        {
            for (int i = 0; i < count; i++)
            {
                double x = padLeft + (count == 1 ? plotW * 0.5 : (i / (double)xSpan) * plotW);
                double normY = Math.Clamp((data[i] - minC) / (maxC - minC), 0.0, 1.0);
                double y = padTop + (1.0 - normY) * plotH;
                var pt = new Point(x, y);

                if (i == 0)
                {
                    lineCtx.BeginFigure(pt, false);
                    fillCtx.BeginFigure(new Point(x, padTop + plotH), true);
                    fillCtx.LineTo(pt);
                }
                else
                {
                    lineCtx.LineTo(pt);
                    fillCtx.LineTo(pt);
                }

                if (i == count - 1)
                {
                    fillCtx.LineTo(new Point(x, padTop + plotH));
                    fillCtx.EndFigure(true);
                }
            }
        }

        context.DrawGeometry(AreaFillBrush, null, fillGeo);
        context.DrawGeometry(null, CurvePen, lineGeo);

        // Draw vertical cursor at SelectedIteration (1-based index into data)
        int selIdx = Math.Clamp(SelectedIteration - 1, 0, count - 1);
        if (SelectedIteration >= 1 && count > 0)
        {
            double cx = padLeft + (count == 1 ? plotW * 0.5 : (selIdx / (double)xSpan) * plotW);
            double normY = Math.Clamp((data[selIdx] - minC) / (maxC - minC), 0.0, 1.0);
            double cy = padTop + (1.0 - normY) * plotH;

            context.DrawLine(CursorPen, new Point(cx, padTop), new Point(cx, padTop + plotH));
            context.DrawEllipse(CursorDotBrush, null, new Point(cx, cy), 3.5, 3.5);
        }

        // Draw min/max labels
        var maxLabel = new FormattedText(
            $"max {maxC:G4}",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            9.5,
            LabelBrush);
        context.DrawText(maxLabel, new Point(padLeft + 2, 2));

        var minLabel = new FormattedText(
            $"min {minC:G4}",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            9.5,
            LabelBrush);
        context.DrawText(minLabel, new Point(padLeft + 2, h - 14));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            e.Pointer.Capture(this);
            ScrubFromPoint(e.GetPosition(this));
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_isDragging)
        {
            ScrubFromPoint(e.GetPosition(this));
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isDragging)
        {
            _isDragging = false;
            e.Pointer.Capture(null);
        }
    }

    private void ScrubFromPoint(Point pt)
    {
        var data = ComplianceHistory;
        if (data is null || data.Count == 0) return;

        double padLeft = 8.0;
        double padRight = 8.0;
        double plotW = Math.Max(1.0, Bounds.Width - padLeft - padRight);
        double t = Math.Clamp((pt.X - padLeft) / plotW, 0.0, 1.0);

        int idx = (int)Math.Round(t * (data.Count - 1));
        int iteration = Math.Clamp(idx + 1, 1, data.Count);
        IterationScrubbed?.Invoke(iteration);
    }
}

