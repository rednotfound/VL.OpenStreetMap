using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using OsmSharp;
using OsmSharp.Streams;

namespace VL.OpenStreetMap;

/// <summary>
/// OSM XML and .osm.pbf into features, through OsmSharp's readers and <see cref="OsmGeometry"/>.
/// Internal: the nodes call it, on a worker thread.
/// </summary>
internal static class OsmSource
{
    /// <summary>Features from OSM XML text, such as an Overpass response.</summary>
    internal static List<IFeature> FromXml(string xml, out BuildReport report, CancellationToken token = default)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        var elements = Collect(new XmlOsmStreamSource(stream), keep: null, token);
        return OsmGeometry.Build(elements, keep: null, out report);
    }

    /// <summary>Features from a .osm or .osm.pbf file, optionally only those meeting an area.</summary>
    internal static List<IFeature> FromFile(string path, Envelope? keep, out BuildReport report, CancellationToken token = default)
    {
        using var stream = File.OpenRead(path);
        OsmStreamSource source = path.EndsWith(".pbf", StringComparison.OrdinalIgnoreCase)
            ? new PBFOsmStreamSource(stream)
            : new XmlOsmStreamSource(stream);
        var elements = Collect(source, keep, token);
        return OsmGeometry.Build(elements, keep, out report);
    }

    // A whole region's .pbf holds millions of elements, so an area is cut while reading
    // (OsmSharp's FilterBox keeps the ways that cross the box whole). The token is checked as the
    // stream goes, so a new read cancels an old one.
    static List<OsmGeo> Collect(OsmStreamSource source, Envelope? keep, CancellationToken token)
    {
        IEnumerable<OsmGeo> elements = keep is null
            ? source
            : source.FilterBox((float)keep.MinX, (float)keep.MaxY, (float)keep.MaxX, (float)keep.MinY, completeWays: true);
        var list = new List<OsmGeo>();
        foreach (var e in elements)
        {
            token.ThrowIfCancellationRequested();
            list.Add(e);
        }
        return list;
    }
}
