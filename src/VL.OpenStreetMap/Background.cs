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
/// <see cref="Start"/> runs the job on the thread pool and returns at once, and the node reads
/// <see cref="Features"/>, <see cref="Status"/> and <see cref="Busy"/> every frame. A newer job
/// cancels an older one, whose result is then thrown away (the generation check), as
/// VL.NetTopologySuite.GeoJSON's reactive reader does.</para>
/// <para>The features stay the same Spread until a job replaces them, so anything downstream that
/// caches on identity (VL's change detection) sees one value per result, not one per frame.</para>
/// </remarks>
internal sealed class Background : IDisposable
{
    public delegate (IReadOnlyList<NtsFeature>? Features, string Status) Job(CancellationToken token);

    readonly object _gate = new();
    CancellationTokenSource? _running;
    int _generation;
    bool _disposed;

    public Spread<NtsFeature> Features { get; private set; } = Spread<NtsFeature>.Empty;
    public string Status { get; private set; } = "";
    public bool Busy { get; private set; }

    /// <summary>Shows a status without running anything, and forgets any job in flight.</summary>
    public void Set(string status, bool clear)
    {
        lock (_gate)
        {
            _generation++;
            _running?.Cancel();
            Busy = false;
            Status = status;
            if (clear) Features = Spread<NtsFeature>.Empty;
        }
    }

    /// <summary>Runs a job on the thread pool. A null Features in its result keeps the last good ones.</summary>
    public void Start(string busyStatus, Job job)
    {
        CancellationToken token;
        int generation;
        lock (_gate)
        {
            if (_disposed) return;
            _running?.Cancel();
            _running?.Dispose();
            _running = new CancellationTokenSource();
            token = _running.Token;
            generation = ++_generation;
            Busy = true;
            Status = busyStatus;
        }

        Task.Run(() =>
        {
            (IReadOnlyList<NtsFeature>? features, string status) result;
            try
            {
                result = job(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                result = (null, e.GetType().Name + ": " + e.Message);
            }
            lock (_gate)
            {
                if (generation != _generation || _disposed) return;
                if (result.features is not null)
                    Features = Spread.Create(result.features.ToArray());
                Status = result.status;
                Busy = false;
            }
        }, token);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _generation++;
            _running?.Cancel();
            _running?.Dispose();
            _running = null;
        }
    }
}
