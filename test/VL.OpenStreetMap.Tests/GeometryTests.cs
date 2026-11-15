using System.IO;
using System.Linq;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using Xunit;

namespace VL.OpenStreetMap.Tests;

/// <summary>
/// OSM elements into geometry: the rules of docs/DESIGN.md, on a real Overpass answer and on
/// hand-written cases. No network.
/// </summary>
public class GeometryTests
{
    static string Fixture(string name) => File.ReadAllText(Path.Combine("fixtures", name));

    static IFeature Find(System.Collections.Generic.List<IFeature> features, string id)
        => features.Single(f => (string)f.Attributes["@id"] == id);

    static System.Collections.Generic.List<IFeature> Xml(string body)
        => OsmSource.FromXml($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><osm version=\"0.6\">{body}</osm>", out _);

    // ── The real answer: Tokyo Station, 2026-11-15 ──────────────────────────

    [Fact]
    public void Every_building_of_the_real_answer_is_a_polygon()
    {
        // OsmSharp.Geo left 38 buildings as lines and made no polygon at all (measured).
        var features = OsmSource.FromXml(Fixture("tokyo-station.osm"), out var report);
        var buildings = features.Where(f => f.Attributes.Exists("building")).ToList();
        Assert.NotEmpty(buildings);
        Assert.All(buildings, b => Assert.True(b.Geometry is Polygon or MultiPolygon, $"{b.Attributes["@id"]} is a {b.Geometry.GeometryType}"));
        Assert.True(report.Polygons > 0);
    }

    [Fact]
    public void Tokyo_Stations_building_of_35_ways_is_one_valid_polygon()
    {
        // Relation 4856156: a multipolygon of 35 outer ways. OsmSharp.Geo dropped it.
        var features = OsmSource.FromXml(Fixture("tokyo-station.osm"), out _);
        var station = Find(features, "relation/4856156");
        Assert.True(station.Geometry is Polygon or MultiPolygon);
        Assert.True(station.Geometry.IsValid);
        Assert.Equal("train_station", station.Attributes["building"]);
    }

    [Fact]
    public void A_building_relation_becomes_its_outline_so_JP_Tower_is_there()
    {
        // Relation 11955761 (type=building): its outline way has no tags of its own.
        var features = OsmSource.FromXml(Fixture("tokyo-station.osm"), out _);
        var tower = Find(features, "relation/11955761");
        Assert.IsType<Polygon>(tower.Geometry);
        Assert.Equal("JP Tower", tower.Attributes["name:en"] ?? tower.Attributes["name"]);
    }

    [Fact]
    public void Pedestrian_squares_are_areas_and_every_feature_is_lon_lat_with_an_id()
    {
        // The sample's closed highways are all areas by the rules: five pedestrian squares with
        // area=yes, indoor steps, a platform. It has no closed ring road; that case is hand-written below.
        var features = OsmSource.FromXml(Fixture("tokyo-station.osm"), out _);
        var squares = features.Where(f => f.Attributes.Exists("highway") && f.Attributes.Exists("area") && (string)f.Attributes["area"] == "yes").ToList();
        Assert.Equal(5, squares.Count);
        Assert.All(squares, s => Assert.IsType<Polygon>(s.Geometry));
        Assert.All(features, f =>
        {
            Assert.Equal(4326, f.Geometry.SRID);
            Assert.InRange(f.Geometry.Coordinate.X, 139.7, 139.8);   // x is longitude
            Assert.InRange(f.Geometry.Coordinate.Y, 35.6, 35.7);
            Assert.StartsWith((string)f.Attributes["@type"] + "/", (string)f.Attributes["@id"]);
        });
    }

    // ── Hand-written cases ──────────────────────────────────────────────────

    const string Square = """
        <node id="1" lat="0" lon="0"/><node id="2" lat="0" lon="4"/><node id="3" lat="4" lon="4"/><node id="4" lat="4" lon="0"/>
        <node id="5" lat="1" lon="1"/><node id="6" lat="1" lon="2"/><node id="7" lat="2" lon="2"/><node id="8" lat="2" lon="1"/>
        """;

    [Fact]
    public void A_courtyard_building_has_its_hole()
    {
        var f = Xml(Square + """
            <way id="10"><nd ref="1"/><nd ref="2"/><nd ref="3"/><nd ref="4"/><nd ref="1"/></way>
            <way id="11"><nd ref="5"/><nd ref="6"/><nd ref="7"/><nd ref="8"/><nd ref="5"/></way>
            <relation id="20"><member type="way" ref="10" role="outer"/><member type="way" ref="11" role="inner"/>
              <tag k="type" v="multipolygon"/><tag k="building" v="yes"/></relation>
            """);
        var building = Assert.Single(f);
        var polygon = Assert.IsType<Polygon>(building.Geometry);
        Assert.Equal(1, polygon.NumInteriorRings);
        Assert.Equal(15, polygon.Area, 9);
    }

    [Fact]
    public void An_outer_ring_in_two_ways_is_joined()
    {
        var f = Xml(Square + """
            <way id="10"><nd ref="1"/><nd ref="2"/><nd ref="3"/></way>
            <way id="12"><nd ref="3"/><nd ref="4"/><nd ref="1"/></way>
            <relation id="20"><member type="way" ref="10" role="outer"/><member type="way" ref="12" role="outer"/>
              <tag k="type" v="multipolygon"/><tag k="landuse" v="grass"/></relation>
            """);
        Assert.Equal(16, Assert.IsType<Polygon>(Assert.Single(f).Geometry).Area, 9);
    }

    [Fact]
    public void An_old_style_multipolygon_takes_its_outer_ways_tags_and_the_way_is_not_doubled()
    {
        var f = Xml(Square + """
            <way id="10"><nd ref="1"/><nd ref="2"/><nd ref="3"/><nd ref="4"/><nd ref="1"/><tag k="building" v="yes"/></way>
            <way id="11"><nd ref="5"/><nd ref="6"/><nd ref="7"/><nd ref="8"/><nd ref="5"/></way>
            <relation id="20"><member type="way" ref="10" role="outer"/><member type="way" ref="11" role="inner"/>
              <tag k="type" v="multipolygon"/></relation>
            """);
        var building = Assert.Single(f);
        Assert.Equal("relation/20", building.Attributes["@id"]);
        Assert.Equal("yes", building.Attributes["building"]);
    }

    [Theory]
    [InlineData("<tag k=\"highway\" v=\"residential\"/>", "LineString")]          // a ring road
    [InlineData("<tag k=\"barrier\" v=\"fence\"/>", "LineString")]                // a fence is not a wall
    [InlineData("<tag k=\"building\" v=\"yes\"/><tag k=\"area\" v=\"no\"/>", "LineString")]
    [InlineData("<tag k=\"highway\" v=\"pedestrian\"/><tag k=\"area\" v=\"yes\"/>", "Polygon")]
    [InlineData("<tag k=\"leisure\" v=\"park\"/>", "Polygon")]
    [InlineData("<tag k=\"natural\" v=\"coastline\"/>", "LineString")]
    public void A_closed_way_is_an_area_only_when_its_tags_say_so(string tags, string type)
    {
        var f = Xml(Square + $"<way id=\"10\"><nd ref=\"1\"/><nd ref=\"2\"/><nd ref=\"3\"/><nd ref=\"4\"/><nd ref=\"1\"/>{tags}</way>");
        Assert.Equal(type, Assert.Single(f).Geometry.GeometryType);
    }

    [Fact]
    public void Untagged_nodes_are_vertices_and_tagged_nodes_are_points()
    {
        var f = Xml(Square + """<node id="9" lat="3" lon="3"><tag k="amenity" v="cafe"/></node><node id="99" lat="3" lon="3"><tag k="created_by" v="JOSM"/></node>""");
        var cafe = Assert.Single(f);
        Assert.Equal("POINT (3 3)", cafe.Geometry.AsText());
    }

    [Fact]
    public void A_relation_missing_a_way_and_a_route_are_counted_not_thrown()
    {
        OsmSource.FromXml("<osm version=\"0.6\">" + Square + """
            <relation id="20"><member type="way" ref="404" role="outer"/><tag k="type" v="multipolygon"/><tag k="building" v="yes"/></relation>
            <relation id="21"><member type="way" ref="404" role=""/><tag k="type" v="route"/><tag k="route" v="bus"/></relation></osm>
            """, out var report);
        Assert.Equal(0, report.Features);
        Assert.Equal(1, report.SkippedRelations);
        Assert.Equal(1, report.Broken);
    }

    [Fact]
    public void A_self_crossing_building_is_repaired_not_dropped()
    {
        var f = Xml(Square + """<way id="10"><nd ref="1"/><nd ref="3"/><nd ref="2"/><nd ref="4"/><nd ref="1"/><tag k="building" v="yes"/></way>""");
        var b = Assert.Single(f);
        Assert.True(b.Geometry.IsValid);
        Assert.True(b.Geometry.Area > 0);
    }
}
