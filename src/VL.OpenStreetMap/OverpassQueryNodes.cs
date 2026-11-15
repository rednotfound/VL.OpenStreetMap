using System;
using System.Globalization;
using System.Linq;
using NetTopologySuite.Geometries;
using VL.Core.Import;

namespace VL.OpenStreetMap;

// The /// text is the hover tooltip in vvvv: short and for the user. The reasoning lives here.
//
// These nodes write Overpass QL so nobody has to learn it first - and show it, so anybody can
// (QuickOSM's "build query" idea, docs/OSM-RESEARCH.md). The query asks for OSM XML, the elements
// matching the filter, then everything they are made of ("out body; >; out skel qt;"), which is
// what OsmGeometry needs to build lines and polygons.
//
// The area is any geometry, in degrees (SRID 4326): its bounding box is what Overpass is asked
// for, because Overpass takes a box. Larger than 0.25 degrees a side (about 25 km) is refused with
// a status instead of a query: the server would take minutes and may stop it anyway, and the
// usage policy asks for planet extracts (ReadOSM) for big areas. Gismo caps at 0.5 degrees.

/// <summary>
/// The OSM features the Preset node can ask for.
/// </summary>
public enum OsmPreset
{
    /// <summary>Every building.</summary>
    Buildings,
    /// <summary>Every road and path.</summary>
    Roads,
    /// <summary>Railway lines, stations and platforms.</summary>
    Railways,
    /// <summary>Lakes, rivers and other water.</summary>
    Water,
    /// <summary>Parks, gardens, grass and woods.</summary>
    Green,
    /// <summary>Shops, cafes, schools and other places to go.</summary>
    Amenities,
    /// <summary>Everything mapped in the area. Keep the area small.</summary>
    Everything,
}

/// <summary>
/// Writes Overpass queries for the Overpass node.
/// </summary>
[SkipCategory]
public static class OverpassQueryNodes
{
    internal const double MaxSide = 0.25;

    /// <summary>
    /// Writes an Overpass query for the features with a tag inside an area.
    /// </summary>
    /// <remarks>
    /// The area is in longitude and latitude; its bounding box is used, at most 0.25 degrees a side.
    /// An empty Value means any value: Key building finds every building.
    /// </remarks>
    /// <param name="area">Where to look, in longitude and latitude.</param>
    /// <param name="key">The tag key, such as building or highway.</param>
    /// <param name="value">The tag value, such as residential. Empty means any.</param>
    /// <param name="status">Why there is no query, when there is none.</param>
    [Name("OverpassQuery (Tag)")]
    [return: Pin(Name = "Query")]
    public static string QueryTag(Geometry? area, [Pin(Name = "Status")] out string status, string key = "building", string? value = null)
        => Box(area, out status) is { } box ? Wrap(Selector("nwr", key, value) + box) : "";

    /// <summary>
    /// Writes an Overpass query for a kind of feature inside an area.
    /// </summary>
    /// <remarks>
    /// The area is in longitude and latitude; its bounding box is used, at most 0.25 degrees a side.
    /// </remarks>
    /// <param name="area">Where to look, in longitude and latitude.</param>
    /// <param name="preset">What to ask for.</param>
    /// <param name="status">Why there is no query, when there is none.</param>
    [Name("OverpassQuery (Preset)")]
    [return: Pin(Name = "Query")]
    public static string QueryPreset(Geometry? area, [Pin(Name = "Status")] out string status, OsmPreset preset = OsmPreset.Buildings)
    {
        if (Box(area, out status) is not { } box) return "";
        var selectors = preset switch
        {
            OsmPreset.Buildings => new[] { "nwr[\"building\"]" },
            OsmPreset.Roads => new[] { "way[\"highway\"]" },
            OsmPreset.Railways => new[] { "nwr[\"railway\"]" },
            OsmPreset.Water => new[] { "nwr[\"natural\"=\"water\"]", "nwr[\"waterway\"]" },
            OsmPreset.Green => new[] { "nwr[\"leisure\"~\"^(park|garden)$\"]", "nwr[\"landuse\"~\"^(grass|forest|meadow|recreation_ground)$\"]", "nwr[\"natural\"~\"^(wood|scrub|grassland)$\"]" },
            OsmPreset.Amenities => new[] { "nwr[\"amenity\"]", "nwr[\"shop\"]" },
            _ => new[] { "nwr" },
        };
        return Wrap(string.Concat(selectors.Select(s => s + box)));
    }

    /// <summary>
    /// Writes an Overpass query for the features with a tag within a distance of a position.
    /// </summary>
    /// <remarks>
    /// Position is longitude and latitude; Radius is in metres, at most 10000.
    /// </remarks>
    /// <param name="position">The centre, as longitude and latitude.</param>
    /// <param name="radius">How far around it, in metres.</param>
    /// <param name="key">The tag key, such as building or highway.</param>
    /// <param name="value">The tag value. Empty means any.</param>
    /// <param name="status">Why there is no query, when there is none.</param>
    [Name("OverpassQuery (Around)")]
    [return: Pin(Name = "Query")]
    public static string QueryAround(Coordinate? position, [Pin(Name = "Status")] out string status, double radius = 300, string key = "building", string? value = null)
    {
        if (position is null || double.IsNaN(position.X) || double.IsNaN(position.Y))
        {
            status = "no position";
            return "";
        }
        if (Math.Abs(position.Y) > 90 || Math.Abs(position.X) > 180)
        {
            status = "the position is not longitude and latitude";
            return "";
        }
        if (!(radius > 0) || radius > 10_000)
        {
            status = "the radius must be between 0 and 10000 metres";
            return "";
        }
        status = "";
        var around = string.Create(CultureInfo.InvariantCulture, $"(around:{radius:0.#},{position.Y:0.######},{position.X:0.######});");
        return Wrap(Selector("nwr", key, value) + around);
    }

    // "(south,west,north,east);" - Overpass's order - or null with a reason.
    static string? Box(Geometry? area, out string status)
    {
        if (area is null || area.IsEmpty)
        {
            status = "no area";
            return null;
        }
        var e = area.EnvelopeInternal;
        if (e.MinY < -90 || e.MaxY > 90 || e.MinX < -180 || e.MaxX > 180)
        {
            status = "the area is not in longitude and latitude (SRID 4326)";
            return null;
        }
        if (e.Width > MaxSide || e.Height > MaxSide)
        {
            status = $"the area is {e.Width:0.###} x {e.Height:0.###} degrees; at most {MaxSide} a side - use a smaller area, or ReadOSM with a downloaded file";
            return null;
        }
        status = "";
        return string.Create(CultureInfo.InvariantCulture, $"({e.MinY:0.######},{e.MinX:0.######},{e.MaxY:0.######},{e.MaxX:0.######});");
    }

    static string Selector(string what, string key, string? value)
    {
        var k = Escape(string.IsNullOrWhiteSpace(key) ? "building" : key.Trim());
        return string.IsNullOrEmpty(value) ? $"{what}[\"{k}\"]" : $"{what}[\"{k}\"=\"{Escape(value.Trim())}\"]";
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // OSM XML, a 25 s timeout (QuickOSM's default), the matches, then everything they are made of.
    internal static string Wrap(string statements)
        => "[out:xml][timeout:25];\n(\n  " + statements.Replace(");", ");\n  ").TrimEnd() + "\n);\nout body;\n>;\nout skel qt;";
}
