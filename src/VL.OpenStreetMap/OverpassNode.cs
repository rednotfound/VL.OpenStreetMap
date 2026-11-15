using System;
using System.Linq;
using VL.Core.Import;
using VL.Lib.Collections;
using NtsFeature = NetTopologySuite.Features.Feature;

namespace VL.OpenStreetMap;

// The /// text is the hover tooltip in vvvv. The reasoning lives here.
//
// Nothing is fetched until Download is pressed: a patch that opens never hits the server
// (VL.Mapsui's fetching nodes start switched off for the same reason). A query that was answered
// before shows its cached answer at once, with no network, and the status says how old it is - so a
// patch that worked once keeps working offline and in a classroom. Download asks again and replaces
// the cached answer. The server is busy (a 502 on the first request, 33 s for the next, measured
// 2026-11-15), so failures are a status, never an exception, and the last good result stays.

/// <summary>
/// Asks an Overpass server for OpenStreetMap features, and keeps every answer.
/// </summary>
/// <remarks>
/// Nothing is fetched until Download is pressed; a query answered before shows its saved answer at once, offline too.
/// Show the Attribution wherever the data is shown.
/// </remarks>
[ProcessNode(Name = "Overpass", Category = "OpenStreetMap")]
public sealed class OverpassNode : IDisposable
{
    readonly Background _work = new();
    string? _query;
    string? _server;

    /// <summary>Gives the features the query found.</summary>
    /// <param name="status">What happened, in words: how many features and how old, or what went wrong.</param>
    /// <param name="busy">True while the server is being asked or an answer is being read.</param>
    /// <param name="query">An Overpass query asking for [out:xml], such as an OverpassQuery node writes.</param>
    /// <param name="download">Asks the server now, and saves the answer.</param>
    /// <param name="server">The Overpass server. Try another when this one is busy.</param>
    [return: Pin(Name = "Features")]
    public Spread<NtsFeature> Update(
        out string status,
        out bool busy,
        string? query = null,
        bool download = false,
        string server = OverpassClient.DefaultServer)
    {
        var q = query?.Trim() ?? "";
        var s = string.IsNullOrWhiteSpace(server) ? OverpassClient.DefaultServer : server.Trim();

        if (q != _query || s != _server)
        {
            _query = q;
            _server = s;
            if (q.Length == 0)
                _work.Set("no query", clear: true);
            else
                _work.Start("reading the saved answer", token =>
                {
                    if (!OverpassClient.TryCached(s, q, out var xml, out var age))
                        return (Array.Empty<NtsFeature>(), "not downloaded yet: press Download");
                    var features = OsmSource.FromXml(xml, out var report, token);
                    return (features.Cast<NtsFeature>().ToList(), $"{report}, saved {Age(age)} ago");
                });
        }

        if (download && q.Length > 0)
        {
            _work.Start($"asking {Host(s)} ...", token =>
            {
                try
                {
                    var xml = OverpassClient.FetchAsync(s, q, token).GetAwaiter().GetResult();
                    var features = OsmSource.FromXml(xml, out var report, token);
                    return (features.Cast<NtsFeature>().ToList(), $"{report}, downloaded just now");
                }
                catch (OverpassException e)
                {
                    var hasSaved = OverpassClient.TryCached(s, q, out _, out _);
                    return (null, e.Message + (hasSaved ? " - showing the saved answer" : ""));
                }
            });
        }

        status = _work.Status;
        busy = _work.Busy;
        return _work.Features;
    }

    static string Host(string server) => Uri.TryCreate(server, UriKind.Absolute, out var u) ? u.Host : server;

    static string Age(TimeSpan age)
        => age.TotalMinutes < 1 ? "seconds"
         : age.TotalHours < 1 ? $"{(int)age.TotalMinutes} min"
         : age.TotalDays < 1 ? $"{(int)age.TotalHours} h"
         : $"{(int)age.TotalDays} days";

    /// <summary>Stops a request in flight.</summary>
    public void Dispose() => _work.Dispose();
}
