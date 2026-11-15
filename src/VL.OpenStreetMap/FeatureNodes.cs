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
// VL.NetTopologySuite's GetAttribute: no node of ours for that.

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

    // The order every tool surveyed uses (Houdini, BlenderGIS): height, then building:levels times a
    // level height. NaN when neither is there - never a random number, which would look like data.
    // OSM heights are metres unless marked: "12", "12 m", "12.5", "40'", "40'6\"", "40 ft".

    /// <summary>
    /// Gives a building's height in metres from its OSM tags.
    /// </summary>
    /// <remarks>
    /// From height, else building:levels times Level Height, else NaN.
    /// Estimated is true when it came from the number of levels.
    /// </remarks>
    /// <param name="feature">The building.</param>
    /// <param name="levelHeight">Metres per level, when only the number of levels is tagged.</param>
    /// <param name="minHeight">Where the building starts above the ground, in metres: 0 for most.</param>
    /// <param name="estimated">True when the height came from the number of levels.</param>
    [return: Pin(Name = "Height")]
    public static double Height(NtsFeature? feature, out double minHeight, out bool estimated, double levelHeight = 3)
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
        if (Number(Tag("building:levels")) is double levels)
        {
            estimated = true;
            return levels * levelHeight + (Number(Tag("roof:levels")) ?? 0) * levelHeight;
        }
        return double.NaN;
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
