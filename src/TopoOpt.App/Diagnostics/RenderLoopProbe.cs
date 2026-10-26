using System;
using System.Diagnostics;

namespace TopoOpt.App.Diagnostics;

public enum RenderLoopState
{
    IdleOnDemand,
    Active,
    Stalled
}

/// <summary>
/// Probes the OpenGL render loop to separate 'idle by design' (on-demand rendering)
/// from a stalled render loop (requested frame never executed).
/// </summary>
public sealed class RenderLoopProbe
{
    private static readonly object SyncLock = new();
    private static RenderLoopProbe? _instance;

    private long _lastFrameRequestedTimestamp;
    private long _lastRenderTimestamp;
    private bool _framePending;
    private double _currentFps;

    public static RenderLoopProbe Instance
    {
        get
        {
            lock (SyncLock)
            {
                return _instance ??= new RenderLoopProbe();
            }
        }
    }

    public void OnFrameRequested()
    {
        _lastFrameRequestedTimestamp = Stopwatch.GetTimestamp();
        _framePending = true;
    }

    public void OnRenderExecuted(double fps)
    {
        _lastRenderTimestamp = Stopwatch.GetTimestamp();
        _framePending = false;
        _currentFps = fps;
    }

    public (RenderLoopState State, string StatusText) GetStatus()
    {
        long now = Stopwatch.GetTimestamp();
        double sinceRenderMs = (now - _lastRenderTimestamp) * 1000.0 / Stopwatch.Frequency;
        double sinceRequestedMs = (now - _lastFrameRequestedTimestamp) * 1000.0 / Stopwatch.Frequency;

        if (_framePending && sinceRequestedMs > 100.0)
        {
            return (RenderLoopState.Stalled, $"STALLED ({sinceRequestedMs:F0} ms)");
        }

        if (sinceRenderMs > 600.0)
        {
            return (RenderLoopState.IdleOnDemand, "Idle (on-demand)");
        }

        return (RenderLoopState.Active, $"{_currentFps:F0} FPS");
    }
}

