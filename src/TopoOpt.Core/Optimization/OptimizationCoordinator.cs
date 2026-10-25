using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TopoOpt.Core.Fem;
using TopoOpt.Core.Geometry;
using TopoOpt.Core.Persistence;
using TopoOpt.Core.PostProcessing;
using TopoOpt.Core.Voxelization;

namespace TopoOpt.Core.Optimization;

/// <summary>
/// Lock-free "latest-wins" slot for the live 3D display consumer.
/// Never blocks the producer and never queues more than 1 pending item.
/// </summary>
public sealed class LatestWinsDisplaySlot<T> where T : class
{
    private T? _latest;
    private int _pendingCount;
    private int _maxObservedPendingCount;
    private int _inFlight;
    private int _extractedCount;

    public int PendingCount => Volatile.Read(ref _pendingCount);
    public int MaxObservedPendingCount => Volatile.Read(ref _maxObservedPendingCount);
    public int ExtractedCount => Volatile.Read(ref _extractedCount);
    public bool IsExtractionInFlight => Volatile.Read(ref _inFlight) != 0;

    public void Publish(T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        T? prev = Interlocked.Exchange(ref _latest, item);
        if (prev is null)
        {
            int newPending = Interlocked.Increment(ref _pendingCount);
            UpdateMax(ref _maxObservedPendingCount, newPending);
        }
    }

    public T? TryTakeLatest()
    {
        T? item = Interlocked.Exchange(ref _latest, null);
        if (item is not null)
        {
            Interlocked.Exchange(ref _pendingCount, 0);
        }
        return item;
    }

    /// <summary>
    /// Polled by a 5-10 Hz timer. If an extraction is already running, returns false immediately
    /// while the slot retains the latest snapshot.
    /// </summary>
    public async Task<bool> PollAndExtractOnThreadPoolAsync(
        Func<T, CancellationToken, Task> extractAndDisplayAsync,
        CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
        {
            return false;
        }

        try
        {
            T? snapshot = TryTakeLatest();
            if (snapshot is null)
            {
                return false;
            }

            await Task.Run(async () =>
            {
                ct.ThrowIfCancellationRequested();
                await extractAndDisplayAsync(snapshot, ct).ConfigureAwait(false);
                Interlocked.Increment(ref _extractedCount);
            }, ct).ConfigureAwait(false);

            return true;
        }
        finally
        {
            Volatile.Write(ref _inFlight, 0);
        }
    }

    private static void UpdateMax(ref int target, int value)
    {
        int current = Volatile.Read(ref target);
        while (value > current)
        {
            int prev = Interlocked.CompareExchange(ref target, value, current);
            if (prev == current) break;
            current = prev;
        }
    }
}

/// <summary>
/// Coordinates timeline scrubbing with a monotonic sequence number and cancellation of stale extractions.
/// Guarantees that rapid out-of-order completion never overwrites a newer seek.
/// </summary>
public sealed class PlaybackScrubber : IDisposable
{
    private readonly object _lock = new();
    private CancellationTokenSource? _currentCts;
    private long _latestSequenceNumber;
    private int _displayedIteration;
    private long _displayedSequenceNumber;

    public int DisplayedIteration => Volatile.Read(ref _displayedIteration);
    public long LatestSequenceNumber => Interlocked.Read(ref _latestSequenceNumber);

    public async Task<bool> SeekIterationAsync(
        int iteration,
        Func<int, CancellationToken, Task<IndexedMesh>> extractMeshAsync,
        Func<int, IndexedMesh, Task> updateViewportAsync,
        CancellationToken externalCt = default)
    {
        CancellationTokenSource cts;
        long mySeq;

        lock (_lock)
        {
            _currentCts?.Cancel();
            _currentCts?.Dispose();
            _currentCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            cts = _currentCts;
            mySeq = Interlocked.Increment(ref _latestSequenceNumber);
        }

        CancellationToken token = cts.Token;

        try
        {
            IndexedMesh mesh = await Task.Run(async () =>
            {
                token.ThrowIfCancellationRequested();
                return await extractMeshAsync(iteration, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);

            // Sequence-number guard: discard if a newer seek was issued while we were extracting
            lock (_lock)
            {
                if (mySeq < _latestSequenceNumber || mySeq <= _displayedSequenceNumber || token.IsCancellationRequested)
                {
                    return false;
                }
                _displayedSequenceNumber = mySeq;
                _displayedIteration = iteration;
            }

            await updateViewportAsync(iteration, mesh).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _currentCts?.Cancel();
            _currentCts?.Dispose();
            _currentCts = null;
        }
    }
}

/// <summary>
/// Dual-consumer orchestrator for topology optimization runs:
/// 1. Bounded persistence channel (capacity 8, Wait backpressure) consumed by a single RunWriter task.
/// 2. Latest-wins display slot polled by a 5-10 Hz timer for background Surface Nets extraction + double-buffered viewport upload.
/// </summary>
public sealed class OptimizationCoordinator : IDisposable
{
    public const int PersistenceChannelCapacity = 8;

    private readonly IterationController _controller = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly LatestWinsDisplaySlot<IterationSnapshot> _displaySlot = new();

    private Channel<IterationSnapshot>? _persistenceChannel;
    private Task? _optimizerTask;
    private Task? _writerTask;
    private RunWriter? _runWriter;
    private int _emittedSnapshotCount;
    private uint _emittedRollingChecksum = 2166136261u;

    public IterationController Controller => _controller;
    public LatestWinsDisplaySlot<IterationSnapshot> DisplaySlot => _displaySlot;
    public RunWriter? ActiveWriter => _runWriter;

    public int EmittedSnapshotCount => Volatile.Read(ref _emittedSnapshotCount);
    public uint EmittedRollingChecksum => Volatile.Read(ref _emittedRollingChecksum);
    public bool IsRunning => _optimizerTask is { IsCompleted: false } || _writerTask is { IsCompleted: false };
    public bool IsPaused => _controller.IsPaused;

    public event Action<IterationSnapshot, long>? IterationPersisted;
    public event Action<IterationSnapshot>? IterationEmitted;
    public event Action<RunStatus, string?>? RunCompleted;

    private sealed class SynchronousCallbackProgress(Action<IterationSnapshot> callback) : IProgress<IterationSnapshot>
    {
        public void Report(IterationSnapshot value) => callback(value);
    }

    public Task<TopologyOptimizationResult> StartRunAsync(
        FeModel model,
        OptimizerOptions options,
        RunWriter runWriter,
        CancellationToken externalCt = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runWriter);

        return StartCoreAsync(
            runWriter,
            (emit, ctrl, token) =>
            {
                var progress = new SynchronousCallbackProgress(emit);
                return TopologyOptimizer.Run(model, options, progress, token, ctrl);
            },
            externalCt);
    }

    /// <summary>
    /// Allows running with a custom or fake snapshot producer (used for the 1 kHz threading smoke test).
    /// </summary>
    public Task<TopologyOptimizationResult> StartCustomProducerAsync(
        RunWriter? runWriter,
        Func<Action<IterationSnapshot>, IterationController, CancellationToken, TopologyOptimizationResult> producer,
        CancellationToken externalCt = default)
    {
        ArgumentNullException.ThrowIfNull(producer);
        return StartCoreAsync(runWriter, producer, externalCt);
    }

    private async Task<TopologyOptimizationResult> StartCoreAsync(
        RunWriter? runWriter,
        Func<Action<IterationSnapshot>, IterationController, CancellationToken, TopologyOptimizationResult> producer,
        CancellationToken externalCt)
    {
        _runWriter = runWriter;
        _controller.Reset();
        _emittedSnapshotCount = 0;
        _emittedRollingChecksum = 2166136261u;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, externalCt);
        CancellationToken token = linkedCts.Token;

        _persistenceChannel = Channel.CreateBounded<IterationSnapshot>(new BoundedChannelOptions(PersistenceChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = true,
            AllowSynchronousContinuations = false
        });

        var channel = _persistenceChannel;

        // Consumer A: Single persistence writer task
        _writerTask = Task.Run(async () =>
        {
            await foreach (IterationSnapshot snap in channel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            {
                if (runWriter != null)
                {
                    await runWriter.WriteSnapshotAsync(snap, isFinal: false, CancellationToken.None).ConfigureAwait(false);
                    IterationPersisted?.Invoke(snap, runWriter.RunningDiskBytes);
                }
                else
                {
                    IterationPersisted?.Invoke(snap, 0L);
                }
            }
        }, CancellationToken.None);

        TopologyOptimizationResult result = new();
        RunStatus finalStatus = RunStatus.Completed;
        string? errorMsg = null;

        try
        {
            // Producer: Optimiser worker thread
            _optimizerTask = Task.Run<TopologyOptimizationResult>(() =>
            {
                return producer(
                    snapshot =>
                    {
                        uint frameHash = RunWriter.ComputeQuantizedChecksum(snapshot.PhysicalDensity);
                        _emittedRollingChecksum = (_emittedRollingChecksum ^ frameHash) * 16777619u;
                        Interlocked.Increment(ref _emittedSnapshotCount);

                        // (b) Display slot: latest-wins non-blocking
                        _displaySlot.Publish(snapshot);
                        IterationEmitted?.Invoke(snapshot);

                        // (a) Persistence channel: bounded capacity 8; waits if full, NEVER drops a snapshot
                        // Note: pass CancellationToken.None so an emitted snapshot is always enqueued before stopping
                        channel.Writer.WriteAsync(snapshot, CancellationToken.None).AsTask().GetAwaiter().GetResult();
                    },
                    _controller,
                    token);
            }, token);

            result = await ((Task<TopologyOptimizationResult>)_optimizerTask).ConfigureAwait(false);

            if (_controller.IsStopped || token.IsCancellationRequested)
            {
                finalStatus = RunStatus.Stopped;
            }
            else
            {
                finalStatus = RunStatus.Completed;
            }
        }
        catch (OperationCanceledException)
        {
            finalStatus = RunStatus.Stopped;
        }
        catch (Exception ex)
        {
            finalStatus = RunStatus.Failed;
            errorMsg = ex.Message;
            throw;
        }
        finally
        {
            channel.Writer.TryComplete();
            try
            {
                if (_writerTask != null)
                {
                    await _writerTask.ConfigureAwait(false);
                }
            }
            finally
            {
                runWriter?.FinalizeRun(finalStatus, errorMsg);
                RunCompleted?.Invoke(finalStatus, errorMsg);
            }
        }

        return result;
    }

    public void Pause() => _controller.Pause();

    public void Resume() => _controller.Resume();

    public async Task StopAndFinalizeAsync()
    {
        _controller.Stop();
        _cts.Cancel();

        if (_optimizerTask != null)
        {
            try { await _optimizerTask.ConfigureAwait(false); } catch { /* ignore */ }
        }
        if (_writerTask != null)
        {
            try { await _writerTask.ConfigureAwait(false); } catch { /* ignore */ }
        }
    }

    public void Dispose()
    {
        _controller.Stop();
        _cts.Cancel();
        _cts.Dispose();
    }
}
