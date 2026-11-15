using System.Collections.Generic;

namespace VL.OpenStreetMap;

/// <summary>
/// Whether a closed way is an area or a line, by its tags. Internal: none of this is a node.
/// </summary>
/// <remarks>
/// <para>OSM has no geometry type: a closed way is a ring of nodes, and only its tags say whether it
/// encloses an area (a building, a park) or is a line that happens to close (a ring road, a fence).
/// These are overpass-turbo's published rules, https://wiki.openstreetmap.org/wiki/Overpass_turbo/Polygon_Features,
/// the de-facto standard that osmtogeojson and overpass-turbo apply. Their author calls them "surely
/// quite incomplete"; they are what every tool the research looked at agrees on.</para>
/// <para>Measured 2026-11-15: OsmSharp.Geo applies no such rule, and left every building of the
/// Tokyo Station sample a line or a bare ring.</para>
/// </remarks>
internal static class PolygonRules
{
    // Taken rule for rule from the published list (27 rules, fetched 2026-11-15):
    // https://raw.githubusercontent.com/tyrasd/osm-polygon-features/master/polygon-features.json
    // "all": any value but "no" makes an area.
    static readonly HashSet<string> AnyValue = new()
    {
        "building", "landuse", "amenity", "leisure", "area", "boundary", "place", "shop", "tourism",
        "historic", "public_transport", "office", "building:part", "military", "ruins", "area:highway",
        "craft", "golf", "indoor",
    };

    // "blacklist": any value but these (and "no") makes an area.
    static readonly Dictionary<string, HashSet<string>> AnyValueExcept = new()
    {
        ["natural"] = new() { "no", "coastline", "cliff", "ridge", "arete", "tree_row" },
        ["man_made"] = new() { "no", "cutline", "embankment", "pipeline" },
        ["aeroway"] = new() { "no", "taxiway" },
    };

    // "whitelist": only these values make an area.
    static readonly Dictionary<string, HashSet<string>> OnlyValues = new()
    {
        ["highway"] = new() { "services", "rest_area", "escape", "elevator" },
        ["barrier"] = new() { "city_wall", "ditch", "hedge", "retaining_wall", "wall", "spikes" },
        ["railway"] = new() { "station", "turntable", "roundhouse", "platform" },
        ["waterway"] = new() { "riverbank", "dock", "boatyard", "dam" },
        ["power"] = new() { "plant", "substation", "generator", "transformer" },
    };

    /// <summary>True when a closed way with these tags encloses an area.</summary>
    internal static bool IsArea(IReadOnlyDictionary<string, string> tags)
    {
        if (tags.TryGetValue("area", out var area))
            return area != "no";

        foreach (var (key, value) in tags)
        {
            if (value == "no") continue;
            if (AnyValue.Contains(key)) return true;
            if (AnyValueExcept.TryGetValue(key, out var except) && !except.Contains(value)) return true;
            if (OnlyValues.TryGetValue(key, out var only) && only.Contains(value)) return true;
        }
        return false;
    }

    // Tags that say nothing about the thing itself, so an element carrying only these is not a
    // feature of its own (an untagged vertex, a multipolygon member way). osmtogeojson keeps the same list.
    static readonly HashSet<string> Uninteresting = new()
    {
        "source", "source_ref", "source:ref", "history", "attribution", "created_by", "fixme", "FIXME",
        "note", "tiger:county", "tiger:tlid", "tiger:upload_uuid",
    };

    /// <summary>True when the tags describe something, so the element is a feature of its own.</summary>
    internal static bool IsInteresting(IReadOnlyDictionary<string, string> tags)
    {
        foreach (var key in tags.Keys)
            if (!Uninteresting.Contains(key))
                return true;
        return false;
    }
}
