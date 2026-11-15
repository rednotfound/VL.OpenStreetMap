using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using Xunit;

namespace VL.OpenStreetMap.Tests;

/// <summary>The query, height and split nodes, and Overpass from its cache. No network.</summary>
public class NodeTests
{
    static readonly GeometryFactory F = new(new PrecisionModel(), 4326);

    [Fact]
    public void A_tag_query_asks_for_the_box_south_west_north_east_and_everything_it_is_made_of()
    {
        var area = F.ToGeometry(new Envelope(139.765, 139.769, 35.6795, 35.683));
        var q = OverpassQueryNodes.QueryTag(area, out var status, "building");
        Assert.Equal("", status);
        Assert.StartsWith("[out:xml][timeout:25];", q);
        Assert.Contains("nwr[\"building\"](35.6795,139.765,35.683,139.769);", q);
        Assert.EndsWith("out body;\n>;\nout skel qt;", q);
    }

    [Fact]
    public void A_value_and_quotes_are_written_safely()
    {
        var area = F.ToGeometry(new Envelope(0, 0.01, 0, 0.01));
        var q = OverpassQueryNodes.QueryTag(area, out _, "name", "Joe's \"Cafe\"");
        Assert.Contains("nwr[\"name\"=\"Joe's \\\"Cafe\\\"\"]", q);
    }

    [Fact]
    public void An_area_too_large_or_not_in_degrees_is_refused_with_a_reason()
    {
        Assert.Equal("", OverpassQueryNodes.QueryTag(F.ToGeometry(new Envelope(139, 140, 35, 36)), out var big));
        Assert.Contains("at most 0.25", big);
        Assert.Equal("", OverpassQueryNodes.QueryTag(F.ToGeometry(new Envelope(390000, 391000, 5800000, 5801000)), out var metres));
        Assert.Contains("longitude and latitude", metres);
        Assert.Equal("", OverpassQueryNodes.QueryTag(null, out var none));
        Assert.Equal("no area", none);
    }

    [Fact]
    public void An_around_query_is_metres_around_lat_lon_in_invariant_culture()
    {
        var old = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");   // decimal comma
        try
        {
            var q = OverpassQueryNodes.QueryAround(new Coordinate(139.7671, 35.6812), out var status, 300.5, "amenity", "cafe");
            Assert.Equal("", status);
            Assert.Contains("nwr[\"amenity\"=\"cafe\"](around:300.5,35.6812,139.7671);", q);
        }
        finally { Thread.CurrentThread.CurrentCulture = old; }
    }

    [Fact]
    public void Every_preset_writes_a_query()
    {
        var area = F.ToGeometry(new Envelope(0, 0.01, 0, 0.01));
        foreach (OsmPreset p in Enum.GetValues<OsmPreset>())
            Assert.Contains("(0,0,0.01,0.01);", OverpassQueryNodes.QueryPreset(area, out _, p));
    }

    [Theory]
    [InlineData("12", 12)]
    [InlineData("12 m", 12)]
    [InlineData("12.5m", 12.5)]
    [InlineData("40'", 12.192)]
    [InlineData("40'6\"", 12.3444)]
    [InlineData("40 ft", 12.192)]
    [InlineData("12,5", 12.5)]
    public void Heights_are_metres_whatever_the_tag_writes(string tag, double metres)
        => Assert.Equal(metres, FeatureNodes.Metres(tag)!.Value, 4);

    static NetTopologySuite.Features.Feature Building(params (string, object)[] tags)
    {
        var a = new AttributesTable();
        foreach (var (k, v) in tags) a.Add(k, v);
        return new NetTopologySuite.Features.Feature(F.CreatePoint(new Coordinate(0, 0)), a);
    }

    [Fact]
    public void BuildingHeight_comes_from_height_then_building_height_then_levels_then_nothing()
    {
        Assert.Equal(50, FeatureNodes.BuildingHeight(Building(("height", "50")), out _, out var e1));
        Assert.False(e1);
        Assert.Equal(20, FeatureNodes.BuildingHeight(Building(("building:height", "20"), ("building:levels", "4")), out _, out var eb));
        Assert.False(eb);
        Assert.Equal(50, FeatureNodes.BuildingHeight(Building(("height", "50"), ("building:height", "20")), out _, out _));
        Assert.Equal(12, FeatureNodes.BuildingHeight(Building(("building:levels", "4")), out _, out var e2));
        Assert.True(e2);
        Assert.True(double.IsNaN(FeatureNodes.BuildingHeight(Building(("building", "yes")), out _, out var e3)));
        Assert.False(e3);
        Assert.True(double.IsNaN(FeatureNodes.BuildingHeight(null, out _, out _)));
        FeatureNodes.BuildingHeight(Building(("height", "30"), ("min_height", "10")), out var min, out _);
        Assert.Equal(10, min);
        FeatureNodes.BuildingHeight(Building(("height", "30"), ("building:min_level", "2")), out var minLevel, out _);
        Assert.Equal(6, minLevel);
    }

    [Fact]
    public void A_Default_is_given_only_when_nothing_is_tagged_and_is_marked_estimated()
    {
        Assert.Equal(9, FeatureNodes.BuildingHeight(Building(("building", "yes")), out _, out var e, defaultHeight: 9));
        Assert.True(e);
        Assert.Equal(50, FeatureNodes.BuildingHeight(Building(("height", "50")), out _, out var e2, defaultHeight: 9));
        Assert.False(e2);
    }

    [Fact]
    public void Filter_by_tag_keeps_a_key_with_any_value_or_with_one_value()
    {
        var cafe = Building(("amenity", "cafe"));
        var bank = Building(("amenity", "bank"));
        var house = Building(("building", "house"));
        var all = new[] { cafe, bank, house, null! };

        var any = FeatureNodes.FilterTag(all, out var notAmenity, "amenity");
        Assert.Equal(new[] { cafe, bank }, any);
        Assert.Equal(new[] { house }, notAmenity);

        var cafes = FeatureNodes.FilterTag(all, out var rest, "amenity", "cafe");
        Assert.Equal(new[] { cafe }, cafes);
        Assert.Equal(new[] { bank, house }, rest);

        Assert.Empty(FeatureNodes.FilterTag(all, out var none, ""));
        Assert.Equal(3, none.Count);
        Assert.Empty(FeatureNodes.FilterTag(null, out var nothing));
        Assert.Empty(nothing);
    }

    [Fact]
    public void Filter_by_tag_reads_the_id_and_type_attributes_too()
    {
        var features = OsmSource.FromXml(File.ReadAllText(Path.Combine("fixtures", "tokyo-station.osm")), out _)
            .Cast<NetTopologySuite.Features.Feature>().ToList();
        var relations = FeatureNodes.FilterTag(features, out var rest, "@type", "relation");
        Assert.NotEmpty(relations);
        Assert.Equal(features.Count, relations.Count + rest.Count);
        Assert.Contains(relations, f => (string)f.Attributes["@id"] == "relation/4856156");
    }

    [Fact]
    public void Split_sorts_points_lines_and_polygons()
    {
        var features = OsmSource.FromXml(File.ReadAllText(Path.Combine("fixtures", "tokyo-station.osm")), out var report)
            .Cast<NetTopologySuite.Features.Feature>().ToList();
        FeatureNodes.SplitByType(features, out var points, out var lines, out var polygons);
        Assert.Equal(report.Points, points.Count);
        Assert.Equal(report.Lines, lines.Count);
        Assert.Equal(report.Polygons, polygons.Count);
        FeatureNodes.SplitByType(null, out var p0, out _, out _);
        Assert.Empty(p0);
    }

    [Fact]
    public void ToGeometry_collects_every_features_geometry_in_lon_lat()
    {
        var features = OsmSource.FromXml(File.ReadAllText(Path.Combine("fixtures", "tokyo-station.osm")), out var report)
            .Cast<NetTopologySuite.Features.Feature>().ToList();
        var all = FeatureNodes.ToGeometry(features);
        Assert.Equal(report.Features, all.NumGeometries);
        Assert.Equal(4326, all.SRID);
        Assert.True(FeatureNodes.ToGeometry(null).IsEmpty);
    }

    [Fact]
    public void Attribution_is_the_credit_the_licence_asks_for()
    {
        Assert.Equal("© OpenStreetMap contributors", FeatureNodes.Attribution(out var link));
        Assert.Equal("https://www.openstreetmap.org/copyright", link);
    }

    // ── Overpass, from its cache only: nothing is fetched without Download ────

    [Fact]
    public void Overpass_shows_a_saved_answer_at_once_and_never_asks_the_server_unasked()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vlosm-test-" + Guid.NewGuid().ToString("N"));
        OverpassClient.CacheDirectory = dir;
        try
        {
            const string server = "https://example.invalid/api/interpreter";   // fails if ever asked
            var query = OverpassQueryNodes.Wrap("nwr[\"building\"](0,0,1,1);");
            Directory.CreateDirectory(dir);
            File.WriteAllText(OverpassClient.CacheFile(server, query), File.ReadAllText(Path.Combine("fixtures", "tokyo-station.osm")));

            using var node = new OverpassNode();
            var first = node.Update(out var status, out var inProgress, out var completed, query, server: server);
            var completedFrames = completed ? 1 : 0;
            var watch = Stopwatch.StartNew();
            while (inProgress && watch.ElapsedMilliseconds < 10_000)
            {
                Thread.Sleep(20);
                first = node.Update(out status, out inProgress, out completed, query, server: server);
                if (completed) completedFrames++;
            }
            Assert.False(inProgress);
            Assert.NotEmpty(first);
            Assert.Contains("saved", status);
            Assert.Equal(1, completedFrames);                                                    // On Completed: one frame
            Assert.Same(first, node.Update(out _, out _, out var again, query, server: server));  // one Spread per answer
            Assert.False(again);
        }
        finally
        {
            OverpassClient.CacheDirectory = Path.Combine(Path.GetTempPath(), "unused");
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Overpass_without_a_saved_answer_says_to_press_Download()
    {
        OverpassClient.CacheDirectory = Path.Combine(Path.GetTempPath(), "vlosm-empty-" + Guid.NewGuid().ToString("N"));
        using var node = new OverpassNode();
        var watch = Stopwatch.StartNew();
        string status; bool inProgress;
        do { node.Update(out status, out inProgress, out _, "[out:xml];node(1);out;", server: "https://example.invalid/"); Thread.Sleep(10); }
        while (inProgress && watch.ElapsedMilliseconds < 5000);
        Assert.Equal("not downloaded yet: press Download", status);
        node.Update(out var none, out _, out _, "", server: "https://example.invalid/");
        Assert.Equal("no query", none);
    }

    [Fact]
    public void ReadOSM_reads_once_on_a_held_Read_and_says_On_Completed_once()
    {
        using var node = new ReadOsmNode();
        var path = new VL.Lib.IO.Path(System.IO.Path.GetFullPath(System.IO.Path.Combine("fixtures", "tokyo-station.osm")));
        var completions = 0;
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 1500)
        {
            node.Update(out _, out _, out var done, path, read: true);    // a toggle held on
            if (done) completions++;
            Thread.Sleep(10);
        }
        var features = node.Update(out var status, out var inProgress, out _, path, read: true);
        Assert.False(inProgress);
        Assert.Equal(1, completions);
        Assert.NotEmpty(features);
        Assert.DoesNotContain("aborted", status);
    }

    // ── The worker: Abort, the snapshot, nothing after Dispose ────────────────

    static Background.State WaitIdle(Background work)
    {
        var watch = Stopwatch.StartNew();
        var s = work.Snapshot();
        while (s.InProgress && watch.ElapsedMilliseconds < 5000) { Thread.Sleep(10); s = work.Snapshot(); }
        return s;
    }

    [Fact]
    public void Abort_stops_a_job_keeps_the_last_features_and_completes_nothing()
    {
        using var work = new Background();
        var one = new[] { Building(("building", "yes")) };
        work.Start("first", _ => Task.FromResult<(IReadOnlyList<NetTopologySuite.Features.Feature>?, string)>((one, "done")));
        var done = WaitIdle(work);
        Assert.Single(done.Features);
        Assert.Equal(1, done.Completed);

        var cancelled = false;
        work.Start("slow", async token =>
        {
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { cancelled = true; throw; }
            return (null, "never");
        });
        Assert.True(work.Snapshot().InProgress);
        work.Abort();
        var after = work.Snapshot();
        Assert.False(after.InProgress);
        Assert.Equal("aborted", after.Status);
        Assert.Same(done.Features, after.Features);
        Thread.Sleep(100);
        Assert.True(cancelled);
        Assert.Equal(1, work.Snapshot().Completed);
    }

    [Fact]
    public void A_newer_job_wins_and_a_failure_is_a_status_that_keeps_the_features()
    {
        using var work = new Background();
        var one = new[] { Building(("building", "yes")) };
        work.Start("a", _ => Task.FromResult<(IReadOnlyList<NetTopologySuite.Features.Feature>?, string)>((one, "a done")));
        var first = WaitIdle(work);

        var release = new TaskCompletionSource();
        work.Start("old", async _ => { await release.Task; return (Array.Empty<NetTopologySuite.Features.Feature>(), "old done"); });
        work.Start("new", _ => throw new InvalidOperationException("boom"));
        release.SetResult();
        var s = WaitIdle(work);
        Thread.Sleep(100);
        s = work.Snapshot();
        Assert.Equal("InvalidOperationException: boom", s.Status);
        Assert.Same(first.Features, s.Features);
        Assert.Equal(2, s.Completed);
    }

    [Fact]
    public void Nothing_lands_after_Dispose()
    {
        var work = new Background();
        var release = new TaskCompletionSource();
        work.Start("x", async _ => { await release.Task; return (new[] { Building(("a", "b")) }, "landed"); });
        work.Dispose();
        release.SetResult();
        Thread.Sleep(100);
        var s = work.Snapshot();
        Assert.Empty(s.Features);
        Assert.Equal(0, s.Completed);
    }
}
