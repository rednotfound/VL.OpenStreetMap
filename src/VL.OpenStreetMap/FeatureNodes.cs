using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NetTopologySuite.Geometries;
using VL.Core.Import;
using VL.Lib.Collections;
using NtsFeature = NetTopologySuite.Features.Feature;

namespace VL.OpenStreetMap;

// Nodes that work on the features Overpass and ReadOSM give. A single tag is read with
// VL.NetTopologySuite's GetAttribute: no node of ours for that. The rule, from the 2026-11-15 survey
// of vvvv packs and 17 OSM tools: tags stay generic (GetAttribute, Filter (Tag)); a node for one
// tag convention only where reading it is real work (units, a fallback across keys), as MIDI's
// GetPitchWheel is - BuildingHeight. No Name, Highway or Lanes getters.

/// <summary>
/// Nodes for OpenStreetMap features.
/// </summary>
[SkipCategory]
public static partial class FeatureNodes
{
    // One list out of Overpass keeps the patch simple; this splits it when a layer wants one kind.
    // QuickOSM's four outputs and Gismo's one type per run both drew complaints (OSM-RESEARCH.md).

    /// <summary>
    /// Splits features into points, lines and polygons.
    /// </summary>
    /// <param name="features">The features to split.</param>
    /// <param name="points">The features that are points.</param>
    /// <param name="lines">The features that are lines.</param>
    /// <param name="polygons">The features that are polygons.</param>
    [Name("Split (By Type)")]
    public static void SplitByType(IEnumerable<NtsFeature>? features, out Spread<NtsFeature> points, out Spread<NtsFeature> lines, out Spread<NtsFeature> polygons)
    {
        var all = features?.Where(f => f?.Geometry is { IsEmpty: false }).ToArray() ?? Array.Empty<NtsFeature>();
        points = Spread.Create(all.Where(f => f.Geometry is Point or MultiPoint).ToArray());
        lines = Spread.Create(all.Where(f => f.Geometry is LineString or MultiLineString).ToArray());
        polygons = Spread.Create(all.Where(f => f.Geometry is Polygon or MultiPolygon).ToArray());
    }

    // Every feature's geometry as one GeometryCollection: what a drawn Geometry IOBox, a Skia layer or
    // an operation over all of them (Union (Spread) of the buildings, their total Area) wants. Nothing in
    // VL.NetTopologySuite turns a spread of features into one geometry; every help patch here needs it.

    /// <summary>
    /// Gives every feature's geometry as one geometry, to draw or measure them together.
    /// </summary>
    /// <param name="features">The features.</param>
    [return: Pin(Name = "Geometry")]
    public static Geometry ToGeometry(IEnumerable<NtsFeature>? features)
    {
        var parts = features?.Select(f => f?.Geometry).Where(g => g is { IsEmpty: false }).Cast<Geometry>().ToArray() ?? Array.Empty<Geometry>();
        return OsmGeometry.Factory.CreateGeometryCollection(parts);
    }

    // Selecting by tag is what every OSM tool surveyed offers (Heron, Caribou, SideFX Labs' OSM Filter,
    // Overpass turbo's MapCSS [k] / [k=v]) and how VL.Rhino.3dm treats its free-form UserStrings
    // (FilterKey, FilterKeyValue): generic, by key, never one node per popular tag. 2026-11-15 survey.
    // A key alone matches any value, as Overpass's [building] does - building=no included.
    // "@id" and "@type" are attributes too, so Key "@type" Value "relation" works.

    /// <summary>
    /// Keeps the features that carry a tag, or a tag with a given value.
    /// </summary>
    /// <remarks>
    /// An empty Value keeps every feature that has the key, whatever its value.
    /// </remarks>
    /// <param name="features">The features to filter.</param>
    /// <param name="key">The tag's key, such as building or amenity.</param>
    /// <param name="value">The value it must have, such as cafe. Empty for any.</param>
    /// <param name="others">The features that do not match.</param>
    /// <returns>The features that match.</returns>
    [Name("Filter (Tag)")]
    [return: Pin(Name = "Matching")]
    public static Spread<NtsFeature> FilterTag(IEnumerable<NtsFeature>? features, out Spread<NtsFeature> others, string key = "building", string? value = null)
    {
        var yes = new List<NtsFeature>();
        var no = new List<NtsFeature>();
        foreach (var f in features ?? Array.Empty<NtsFeature>())
        {
            if (f is null) continue;
            (Matches(f, key, value) ? yes : no).Add(f);
        }
        others = Spread.Create(no.ToArray());
        return Spread.Create(yes.ToArray());
    }

    static bool Matches(NtsFeature f, string? key, string? value)
    {
        if (string.IsNullOrEmpty(key) || f.Attributes is not { } a || !a.Exists(key))
            return false;
        return string.IsNullOrEmpty(value) || string.Equals(a[key]?.ToString(), value, StringComparison.Ordinal);
    }

    // The order the 3D tools surveyed use (Caribou: height, building:height, then levels; Heron, SideFX,
    // BlenderGIS, Cesium the same without building:height), levels times a level height (3 m in five of
    // them, 3.66 in OpenMapTiles). When nothing is tagged they all invent a number (0, 5 m, 6 m, 20 m or
    // random) and users then ask why every building is the same height; here it is NaN unless the user
    // gives a Default, and Estimated says it was not mapped. Named BuildingHeight, not Height: a bare
    // Height in the NodeBrowser sits among every Skia and Stride Height, and this one reads building tags.
    // OSM heights are metres unless marked: "12", "12 m", "12.5", "40'", "40'6\"", "40 ft".

    /// <summary>
    /// Gives a building's height in metres from its OSM tags.
    /// </summary>
    /// <remarks>
    /// From height, else building:height, else building:levels times Level Height, else Default.
    /// Estimated is true when the height was not mapped as a height.
    /// </remarks>
    /// <param name="feature">The building.</param>
    /// <param name="levelHeight">Metres per level, when only the number of levels is tagged.</param>
    /// <param name="defaultHeight">The height to give when nothing is tagged. NaN gives NaN.</param>
    /// <param name="minHeight">Where the building starts above the ground, in metres: 0 for most.</param>
    /// <param name="estimated">True when the height came from the levels or from Default.</param>
    [return: Pin(Name = "Height")]
    public static double BuildingHeight(NtsFeature? feature, out double minHeight, out bool estimated, double levelHeight = 3, [Pin(Name = "Default")] double defaultHeight = double.NaN)
    {
        estimated = false;
        minHeight = 0;
        if (feature?.Attributes is not { } a)
            return double.NaN;

        string? Tag(string key) => a.Exists(key) ? a[key]?.ToString() : null;

        if (Metres(Tag("min_height")) is double min) minHeight = min;
        else if (Number(Tag("building:min_level")) is double minLevels) minHeight = minLevels * levelHeight;

        if (Metres(Tag("height")) is double h)
            return h;
        if (Metres(Tag("building:height")) is double bh)
            return bh;
        if (Number(Tag("building:levels")) is double levels)
        {
            estimated = true;
            return levels * levelHeight + (Number(Tag("roof:levels")) ?? 0) * levelHeight;
        }
        estimated = !double.IsNaN(defaultHeight);
        return defaultHeight;
    }

    [GeneratedRegex(@"^\s*(?<feet>\d+(?:\.\d+)?)\s*'\s*(?:(?<inches>\d+(?:\.\d+)?)\s*""?)?\s*$")]
    private static partial Regex FeetAndInches();

    internal static double? Metres(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim().Replace(',', '.');
        var fi = FeetAndInches().Match(t);
        if (fi.Success)
            return double.Parse(fi.Groups["feet"].Value, CultureInfo.InvariantCulture) * 0.3048
                 + (fi.Groups["inches"].Success ? double.Parse(fi.Groups["inches"].Value, CultureInfo.InvariantCulture) * 0.0254 : 0);
        var factor = 1.0;
        if (t.EndsWith("ft", StringComparison.OrdinalIgnoreCase)) { factor = 0.3048; t = t[..^2]; }
        else if (t.EndsWith("m", StringComparison.OrdinalIgnoreCase)) t = t[..^1];
        return double.TryParse(t.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= 0 ? v * factor : null;
    }

    static double? Number(string? text)
        => double.TryParse(text?.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : null;

    // The ODbL asks for credit wherever the data is shown (osmfoundation.org/wiki/Licence/Attribution_Guidelines).
    // None of the tools the research looked at puts it in the patch; this does.

    /// <summary>
    /// Gives the credit OpenStreetMap data must carry wherever it is shown.
    /// </summary>
    /// <param name="link">The page to link the credit to.</param>
    /// <returns>The credit text.</returns>
    [return: Pin(Name = "Text")]
    public static string Attribution(out string link)
    {
        link = "https://www.openstreetmap.org/copyright";
        return "© OpenStreetMap contributors";
    }
}
