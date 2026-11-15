using System;
using System.IO;
using System.Linq;
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

    /// <summary>Gives the features in the file.</summary>
    /// <param name="status">What happened, in words: how many features, or what went wrong.</param>
    /// <param name="busy">True while the file is being read.</param>
    /// <param name="path">A .osm or .osm.pbf file.</param>
    /// <param name="area">Optional: only what lies in this area, in longitude and latitude.</param>
    /// <param name="read">Reads the file again.</param>
    [return: Pin(Name = "Features")]
    public Spread<NtsFeature> Update(
        out string status,
        out bool busy,
        VLPath? path = null,
        Geometry? area = null,
        bool read = false)
    {
        var p = path?.Value ?? "";
        var a = area is { IsEmpty: false } ? area.EnvelopeInternal : null;

        if (read || p != _path || !Equals(a, _area))
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
                    return (features.Cast<NtsFeature>().ToList(), report.ToString());
                });
        }

        status = _work.Status;
        busy = _work.Busy;
        return _work.Features;
    }

    /// <summary>Stops a read in flight.</summary>
    public void Dispose() => _work.Dispose();
}
