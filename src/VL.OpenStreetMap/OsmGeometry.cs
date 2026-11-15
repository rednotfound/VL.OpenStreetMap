using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Operation.Linemerge;
using OsmSharp;

namespace VL.OpenStreetMap;

/// <summary>What a build kept and what it left out, for the Status pin.</summary>
internal sealed record BuildReport(int Points, int Lines, int Polygons, int SkippedRelations, int Broken)
{
    public int Features => Points + Lines + Polygons;

    public override string ToString()
    {
        var text = $"{Features} features ({Points} points, {Lines} lines, {Polygons} polygons)";
        if (SkippedRelations > 0) text += $", {SkippedRelations} relations not features (routes and the like)";
        if (Broken > 0) text += $", {Broken} incomplete or broken left out";
        return text;
    }
}

/// <summary>
/// OSM elements into NetTopologySuite features. Internal: the nodes call it.
/// </summary>
/// <remarks>
/// <para>OSM has nodes (a position), ways (a list of node ids) and relations (a list of members with
/// roles); none of them is a geometry. The rules here, in docs/DESIGN.md:</para>
/// <list type="number">
/// <item><description>A node whose tags describe something is a Point; an untagged node is only a vertex.</description></item>
/// <item><description>An open way is a LineString; a closed one a Polygon when <see cref="PolygonRules"/> says so.</description></item>
/// <item><description>A multipolygon or boundary relation is a Polygon or MultiPolygon: its outer and inner
/// ways are joined into rings (LineMerger), holes go into the outer ring that contains them, and an invalid
/// result is repaired (GeometryFixer) rather than dropped.</description></item>
/// <item><description>A building relation (Simple 3D Buildings) is its outline member, with the relation's tags.</description></item>
/// <item><description>Every other relation is not a feature, and the report counts it.</description></item>
/// </list>
/// <para>Every feature carries all of its tags, plus "@id" ("way/123") and "@type". Geometry is
/// SRID 4326, x = longitude, y = latitude.</para>
/// </remarks>
internal static class OsmGeometry
{
    internal static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    sealed class WayData
    {
        public required long Id;
        public required long[] Nodes;
        public required Dictionary<string, string> Tags;
    }

    /// <summary>Builds the features of a set of OSM elements, in any order.</summary>
    /// <param name="elements">Nodes, ways and relations, complete or not.</param>
    /// <param name="keep">Optional: only features whose bounding box meets this envelope.</param>
    /// <param name="report">What was kept and what was left out.</param>
    internal static List<IFeature> Build(IEnumerable<OsmGeo> elements, Envelope? keep, out BuildReport report)
    {
        var coordinates = new Dictionary<long, Coordinate>();
        var taggedNodes = new List<Node>();
        var ways = new Dictionary<long, WayData>();
        var relations = new List<Relation>();

        foreach (var e in elements)
        {
            switch (e)
            {
                case Node n when n.Id is long id && n.Latitude is double lat && n.Longitude is double lon:
                    coordinates[id] = new Coordinate(lon, lat);
                    if (n.Tags is { Count: > 0 }) taggedNodes.Add(n);
                    break;
                case Way w when w.Id is long id && w.Nodes is { Length: > 0 }:
                    ways[id] = new WayData { Id = id, Nodes = w.Nodes, Tags = TagsOf(w) };
                    break;
                case Relation r when r.Id is not null && r.Members is { Length: > 0 }:
                    relations.Add(r);
                    break;
            }
        }

        var features = new List<IFeature>();
        int points = 0, lines = 0, polygons = 0, skipped = 0, broken = 0;

        void Add(Geometry geometry, Dictionary<string, string> tags, string type, long id)
        {
            if (geometry.IsEmpty) { broken++; return; }
            if (keep is not null && !keep.Intersects(geometry.EnvelopeInternal)) return;
            var attributes = new AttributesTable();
            foreach (var (k, v) in tags) attributes.Add(k, v);
            attributes.Add("@id", $"{type}/{id}");
            attributes.Add("@type", type);
            features.Add(new Feature(geometry, attributes));
            switch (geometry)
            {
                case Point: points++; break;
                case LineString or MultiLineString: lines++; break;
                default: polygons++; break;
            }
        }

        // Relations first: an old-style multipolygon takes its outer way's tags, and that way is then
        // not a feature of its own (it would be the same thing twice).
        var consumedWays = new HashSet<long>();
        var relationFeatures = new List<(Geometry Geometry, Dictionary<string, string> Tags, long Id)>();
        foreach (var r in relations)
        {
            var tags = TagsOf(r);
            tags.TryGetValue("type", out var type);
            switch (type)
            {
                case "multipolygon" or "boundary":
                {
                    var geometry = Multipolygon(r, ways, coordinates);
                    if (geometry is null) { broken++; continue; }
                    if (!PolygonRules.IsInteresting(tags.Where(t => t.Key != "type").ToDictionary(t => t.Key, t => t.Value)))
                    {
                        // Old style: the tags are on the outer way.
                        var outer = r.Members.Where(m => m.Type == OsmGeoType.Way && (m.Role is "outer" or "" or null))
                                             .Select(m => ways.GetValueOrDefault(m.Id)).FirstOrDefault(w => w is not null && PolygonRules.IsInteresting(w.Tags));
                        if (outer is null) { broken++; continue; }
                        foreach (var (k, v) in outer.Tags) tags[k] = v;
                        consumedWays.Add(outer.Id);
                    }
                    relationFeatures.Add((geometry, tags, r.Id!.Value));
                    break;
                }
                case "building":
                {
                    var outline = r.Members.FirstOrDefault(m => m.Role == "outline");
                    Geometry? geometry = null;
                    if (outline is not null && outline.Type == OsmGeoType.Way && ways.TryGetValue(outline.Id, out var w))
                        geometry = Ring(w, coordinates) is { } ring ? Factory.CreatePolygon(ring) : null;
                    else if (outline is not null && outline.Type == OsmGeoType.Relation)
                        geometry = relations.FirstOrDefault(x => x.Id == outline.Id) is { } inner ? Multipolygon(inner, ways, coordinates) : null;
                    if (geometry is null) { broken++; continue; }
                    relationFeatures.Add((geometry, tags, r.Id!.Value));
                    break;
                }
                default:
                    skipped++;
                    break;
            }
        }

        foreach (var n in taggedNodes)
        {
            var tags = TagsOf(n);
            if (PolygonRules.IsInteresting(tags))
                Add(Factory.CreatePoint(coordinates[n.Id!.Value].Copy()), tags, "node", n.Id!.Value);
        }

        foreach (var w in ways.Values)
        {
            if (consumedWays.Contains(w.Id) || !PolygonRules.IsInteresting(w.Tags)) continue;
            var line = Line(w, coordinates);
            if (line is null) { broken++; continue; }
            if (line.IsClosed && line.NumPoints >= 4 && PolygonRules.IsArea(w.Tags))
                Add(Valid(Factory.CreatePolygon(Factory.CreateLinearRing(line.CoordinateSequence))), w.Tags, "way", w.Id);
            else
                Add(line, w.Tags, "way", w.Id);
        }

        foreach (var (geometry, tags, id) in relationFeatures)
            Add(geometry, tags, "relation", id);

        report = new BuildReport(points, lines, polygons, skipped, broken);
        return features;
    }

    static Dictionary<string, string> TagsOf(OsmGeo e)
    {
        var tags = new Dictionary<string, string>();
        if (e.Tags is not null)
            foreach (var t in e.Tags)
                tags[t.Key] = t.Value;
        return tags;
    }

    // A way's coordinates, or null when a node is missing (a way cut at the edge of a file's area).
    static LineString? Line(WayData w, Dictionary<long, Coordinate> coordinates)
    {
        var cs = new Coordinate[w.Nodes.Length];
        for (var i = 0; i < cs.Length; i++)
        {
            if (!coordinates.TryGetValue(w.Nodes[i], out var c)) return null;
            cs[i] = c.Copy();
        }
        return cs.Length >= 2 ? Factory.CreateLineString(cs) : null;
    }

    static LinearRing? Ring(WayData w, Dictionary<long, Coordinate> coordinates)
        => Line(w, coordinates) is { IsClosed: true, NumPoints: >= 4 } line ? Factory.CreateLinearRing(line.CoordinateSequence) : null;

    // Outer and inner ways joined into rings, holes into the outer ring that contains them.
    // A member way may be one piece of a ring (Tokyo Station's Marunouchi building is 35 of them).
    static Geometry? Multipolygon(Relation r, Dictionary<long, WayData> ways, Dictionary<long, Coordinate> coordinates)
    {
        List<LinearRing>? Rings(Func<string?, bool> role)
        {
            var merger = new LineMerger();
            var any = false;
            foreach (var m in r.Members.Where(m => m.Type == OsmGeoType.Way && role(m.Role)))
            {
                if (!ways.TryGetValue(m.Id, out var w)) return null;
                var line = Line(w, coordinates);
                if (line is null) return null;
                merger.Add(line);
                any = true;
            }
            if (!any) return new List<LinearRing>();
            var rings = new List<LinearRing>();
            foreach (LineString merged in merger.GetMergedLineStrings())
            {
                if (!merged.IsClosed || merged.NumPoints < 4) return null;
                rings.Add(Factory.CreateLinearRing(merged.CoordinateSequence));
            }
            return rings;
        }

        var outers = Rings(role => role is "outer" or "" or null);
        var inners = Rings(role => role is "inner");
        if (outers is null || inners is null || outers.Count == 0) return null;

        var shells = outers.Select(o => (Ring: o, Area: Factory.CreatePolygon(o), Holes: new List<LinearRing>())).ToList();
        foreach (var inner in inners)
        {
            var host = shells.Where(s => s.Area.Covers(inner)).OrderBy(s => s.Area.Area).FirstOrDefault();
            host.Holes?.Add(inner);
        }
        var polygons = shells.Select(s => Factory.CreatePolygon(s.Ring, s.Holes.ToArray())).ToArray();
        Geometry result = polygons.Length == 1 ? polygons[0] : Factory.CreateMultiPolygon(polygons);
        return Valid(result);
    }

    // A self-touching or overlapping ring is repaired, not dropped: OSM data is drawn by people.
    static Geometry Valid(Geometry g)
    {
        if (g.IsValid) return g;
        var fixedUp = GeometryFixer.Fix(g);
        fixedUp.SRID = 4326;
        return fixedUp;
    }
}
