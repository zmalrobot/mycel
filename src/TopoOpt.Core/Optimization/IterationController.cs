using System;
using System.Threading;

namespace TopoOpt.Core.Optimization;

/// <summary>
/// Thread-safe controller for cooperative Pause, Resume, and Stop between topology optimization iterations.
/// </summary>
public sealed class IterationController
{
    private readonly ManualResetEventSlim _pauseEvent = new(true);
    private volatile bool _isStopped;
    private volatile bool _isPaused;

    public bool IsPaused => _isPaused;
    public bool IsStopped => _isStopped;

    public void Pause()
    {
        _isPaused = true;
        _pauseEvent.Reset();
    }

    public void Resume()
    {
        _isPaused = false;
        _pauseEvent.Set();
    }

    public void Stop()
    {
        _isStopped = true;
        _isPaused = false;
        _pauseEvent.Set();
    }

    public void Reset()
    {
        _isStopped = false;
        _isPaused = false;
        _pauseEvent.Set();
    }

    /// <summary>
    /// Waits if paused, and returns true if execution should stop.
    /// </summary>
    public bool CheckAndBlock(CancellationToken ct = default)
    {
        if (_isStopped || ct.IsCancellationRequested)
            return true;

        if (_isPaused)
        {
            try
            {
                _pauseEvent.Wait(ct);
            }
            catch (OperationCanceledException)
            {
                return true;
            }
        }

        return _isStopped || ct.IsCancellationRequested;
    }
}
