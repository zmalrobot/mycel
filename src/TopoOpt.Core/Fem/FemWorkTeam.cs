using System;
using System.Threading;

namespace TopoOpt.Core.Fem;

/// <summary>
/// Dedicated zero-allocation parallel worker team for matrix-free FEM matvecs and PCG reductions.
/// Bypasses the .NET Task Parallel Library to eliminate per-invocation heap allocations (RangeWorker/Task).
/// Worker threads are persistent background threads with static chunk partitioning.
/// </summary>
public sealed class FemWorkTeam : IDisposable
{
    public static readonly FemWorkTeam Shared = new();

    private readonly int _threadCount;
    private readonly Thread[] _threads;
    private readonly ManualResetEventSlim[] _startEvents;
    private readonly CountdownEvent _doneEvent;
    private volatile bool _disposed;

    private Action<int>? _currentAction;
    private int _totalChunks;

    public int ThreadCount => _threadCount;

    public FemWorkTeam(int degreeOfParallelism = -1)
    {
        _threadCount = degreeOfParallelism > 0 ? degreeOfParallelism : Math.Max(1, Environment.ProcessorCount);
        _threads = new Thread[_threadCount - 1];
        _startEvents = new ManualResetEventSlim[_threadCount - 1];
        _doneEvent = new CountdownEvent(_threadCount);

        for (int i = 0; i < _threadCount - 1; i++)
        {
            _startEvents[i] = new ManualResetEventSlim(false);
            int workerIndex = i + 1; // Thread 0 is the calling thread
            _threads[i] = new Thread(() => WorkerLoop(workerIndex))
            {
                IsBackground = true,
                Name = $"FemWorker-{workerIndex}"
            };
            _threads[i].Start();
        }
    }

    private void WorkerLoop(int workerId)
    {
        var startEvent = _startEvents[workerId - 1];
        while (!_disposed)
        {
            startEvent.Wait();
            startEvent.Reset();
            if (_disposed) break;

            ExecuteChunks(workerId);
            _doneEvent.Signal();
        }
    }

    private void ExecuteChunks(int threadId)
    {
        var action = _currentAction!;
        int total = _totalChunks;
        int count = _threadCount;

        int chunkSize = (total + count - 1) / count;
        int start = threadId * chunkSize;
        int end = Math.Min(start + chunkSize, total);

        for (int i = start; i < end; i++)
        {
            action(i);
        }
    }

    /// <summary>
    /// Executes chunked parallel loop [0, totalChunks) with zero memory allocations.
    /// </summary>
    public void For(int totalChunks, Action<int> action)
    {
        if (totalChunks <= 0) return;
        if (totalChunks == 1 || _threadCount == 1)
        {
            for (int i = 0; i < totalChunks; i++) action(i);
            return;
        }

        _currentAction = action;
        _totalChunks = totalChunks;
        _doneEvent.Reset(_threadCount);

        for (int i = 0; i < _threadCount - 1; i++)
        {
            _startEvents[i].Set();
        }

        ExecuteChunks(0);
        _doneEvent.Signal();

        _doneEvent.Wait();
        _currentAction = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        for (int i = 0; i < _startEvents.Length; i++)
        {
            _startEvents[i].Set();
        }
        _doneEvent.Dispose();
        for (int i = 0; i < _startEvents.Length; i++)
        {
            _startEvents[i].Dispose();
        }
    }
}

