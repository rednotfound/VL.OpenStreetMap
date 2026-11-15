using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VL.Lib.Collections;
using NtsFeature = NetTopologySuite.Features.Feature;

namespace VL.OpenStreetMap;

/// <summary>
/// The work of a process node on a worker thread, and its last good result. Internal.
/// </summary>
/// <remarks>
/// <para>A node runs sixty times a second, so it may never wait for the network or a large file:
/// <see cref="Start"/> runs the job on the thread pool and returns at once, and the node takes a
/// <see cref="Snapshot"/> every frame. A newer job cancels an older one, whose result is then thrown
/// away (the generation check), as VL.CoreLib's AsyncTask region does: the latest request wins.</para>
/// <para>The shape is vvvv's own for loading (2026-11-15 survey: HTTPGet, FileReader, FileDownloader,
/// TextureReader (Async)): a process node that polls, the last result held as the same instance
/// (what HoldLatest does), In Progress, a one-frame On Completed, and an Abort. Observables are the
/// secondary variant there and are not offered here.</para>
/// <para>Everything the node shows is read in ONE snapshot under the lock, so a frame never shows a
/// new status beside old features. The features are built completely on the worker and never
/// changed after they are published.</para>
/// </remarks>
internal sealed class Background : IDisposable
{
    public delegate Task<(IReadOnlyList<NtsFeature>? Features, string Status, bool Success)> Job(CancellationToken token);

    public readonly record struct State(Spread<NtsFeature> Features, bool Success, string Status, bool InProgress, int Completed);

    readonly object _gate = new();
    CancellationTokenSource? _running;
    int _generation;
    int _completed;
    bool _disposed;
    Spread<NtsFeature> _features = Spread<NtsFeature>.Empty;
    string _status = "";
    bool _success;
    bool _inProgress;

    /// <summary>What the node shows this frame, read together.</summary>
    public State Snapshot()
    {
        lock (_gate) return new State(_features, _success, _status, _inProgress, _completed);
    }

    /// <summary>Shows a status without running anything, and forgets any job in flight.</summary>
    public void Set(string status, bool clear)
    {
        lock (_gate)
        {
            StopRunning();
            _success = false;
            _status = status;
            if (clear) _features = Spread<NtsFeature>.Empty;
        }
    }

    /// <summary>Stops a job in flight and keeps the last good features.</summary>
    public void Abort()
    {
        lock (_gate)
        {
            if (!_inProgress) return;
            StopRunning();
            _success = false;
            _status = "aborted";
        }
    }

    /// <summary>Runs a job on the thread pool. A null Features in its result keeps the last good ones; Success says whether the job worked.</summary>
    public void Start(string inProgressStatus, Job job)
    {
        CancellationToken token;
        int generation;
        lock (_gate)
        {
            if (_disposed) return;
            StopRunning();
            _running = new CancellationTokenSource();
            token = _running.Token;
            generation = _generation;
            _inProgress = true;
            _status = inProgressStatus;
        }

        // Task.Run so that a job's synchronous part (parsing a large file) is off the main thread too;
        // the job itself awaits its I/O rather than blocking a pool thread on it.
        _ = Task.Run(async () =>
        {
            (IReadOnlyList<NtsFeature>? features, string status, bool success) result;
            try
            {
                result = await job(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                result = (null, e.GetType().Name + ": " + e.Message, false);
            }
            var published = result.features is null ? null : Spread.Create(result.features.ToArray());
            lock (_gate)
            {
                if (generation != _generation || _disposed) return;
                if (published is not null) _features = published;
                _status = result.status;
                _success = result.success;
                _inProgress = false;
                _completed++;
            }
        });
    }

    // Under the lock: a new generation, so a job still running finds its result unwanted.
    void StopRunning()
    {
        _generation++;
        _running?.Cancel();
        _running?.Dispose();
        _running = null;
        _inProgress = false;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            StopRunning();
        }
    }
}
