using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NetTopologySuite.Geometries;
using VL.Core.Import;
using VL.Lib.Collections;
using NtsFeature = NetTopologySuite.Features.Feature;
using VLPath = VL.Lib.IO.Path;

namespace VL.OpenStreetMap;

// Offline OSM: a .osm file (what openstreetmap.org's Export gives) or a .osm.pbf extract (Geofabrik,
// BBBike). Reliable in a classroom where the public Overpass server is busy, and the way the usage
// policy asks for big areas. A region's .pbf is large, so an Area cuts while reading (OsmSharp's
// FilterBox), and reading runs on a worker thread. It reads again when the path or the area change,
// and on Read: a local file costs nobody anything.

/// <summary>
/// Reads OpenStreetMap features from a .osm or .osm.pbf file.
/// </summary>
/// <remarks>
/// Connect an Area to keep only what lies in it; a whole region's file is large.
/// Show the Attribution wherever the data is shown.
/// </remarks>
[ProcessNode(Name = "ReadOSM", Category = "OpenStreetMap")]
public sealed class ReadOsmNode : IDisposable
{
    readonly Background _work = new();
    string? _path;
    Envelope? _area;
    int _seenCompleted;
    bool _lastRead;

    // Pins named as vvvv's FileReader names them: In Progress, On Completed, Abort (Busy until 2026-11-15).
    // Read starts on its rising edge only, so a toggle held on does not restart the read every frame.

    /// <summary>Gives the features in the file.</summary>
    /// <param name="status">What happened, in words: how many features, or what went wrong.</param>
    /// <param name="inProgress">True while the file is being read.</param>
    /// <param name="onCompleted">True for one frame when the file has been read, or reading failed.</param>
    /// <param name="path">A .osm or .osm.pbf file.</param>
    /// <param name="area">Optional: only what lies in this area, in longitude and latitude.</param>
    /// <param name="read">Reads the file again.</param>
    /// <param name="abort">Stops reading. The last features stay.</param>
    [return: Pin(Name = "Features")]
    public Spread<NtsFeature> Update(
        out string status,
        out bool inProgress,
        out bool onCompleted,
        VLPath? path = null,
        Geometry? area = null,
        bool read = false,
        bool abort = false)
    {
        var p = path?.Value ?? "";
        var a = area is { IsEmpty: false } ? area.EnvelopeInternal : null;

        var pressed = read && !_lastRead;
        _lastRead = read;
        if (pressed || p != _path || !Equals(a, _area))
        {
            _path = p;
            _area = a;
            if (p.Length == 0)
                _work.Set("no file", clear: true);
            else if (!File.Exists(p))
                _work.Set("no such file: " + p, clear: true);
            else
                _work.Start("reading " + Path.GetFileName(p), token =>
                {
                    var features = OsmSource.FromFile(p, a, out var report, token);
                    return Task.FromResult<(IReadOnlyList<NtsFeature>?, string)>((features.Cast<NtsFeature>().ToList(), report.ToString()));
                });
        }
        if (abort)
            _work.Abort();

        var now = _work.Snapshot();
        status = now.Status;
        inProgress = now.InProgress;
        onCompleted = now.Completed != _seenCompleted;
        _seenCompleted = now.Completed;
        return now.Features;
    }

    /// <summary>Stops a read in flight.</summary>
    public void Dispose() => _work.Dispose();
}
