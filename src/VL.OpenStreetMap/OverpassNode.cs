using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
    int _seenCompleted;
    bool _lastDownload;

    // Pins named and ordered as vvvv's own loaders do (HTTPGet, HTTPPost, FileReader, FileDownloader):
    // the trigger last, In Progress, a one-frame On Completed, an Abort. Busy until 2026-11-15.
    // Download starts on its rising edge only: a toggle held on would otherwise cancel and restart
    // the request every frame, and it would never finish (found by the 2026-11-15 convention review).

    /// <summary>Gives the features the query found.</summary>
    /// <param name="status">What happened, in words: how many features and how old, or what went wrong.</param>
    /// <param name="inProgress">True while the server is being asked or an answer is being read.</param>
    /// <param name="onCompleted">True for one frame when an answer has arrived, or the asking failed.</param>
    /// <param name="query">An Overpass query asking for [out:xml], such as an OverpassQuery node writes.</param>
    /// <param name="server">The Overpass server. Try another when this one is busy.</param>
    /// <param name="download">Asks the server now, and saves the answer.</param>
    /// <param name="abort">Stops asking. The last features stay.</param>
    [return: Pin(Name = "Features")]
    public Spread<NtsFeature> Update(
        out string status,
        out bool inProgress,
        out bool onCompleted,
        string? query = null,
        string server = OverpassClient.DefaultServer,
        bool download = false,
        bool abort = false)
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
                        return Done(Array.Empty<NtsFeature>(), "not downloaded yet: press Download");
                    var features = OsmSource.FromXml(xml, out var report, token);
                    return Done(features.Cast<NtsFeature>().ToList(), $"{report}, saved {Age(age)} ago");
                });
        }

        var pressed = download && !_lastDownload;
        _lastDownload = download;
        if (abort)
            _work.Abort();
        else if (pressed && q.Length > 0)
        {
            _work.Start($"asking {Host(s)} ...", async token =>
            {
                try
                {
                    var xml = await OverpassClient.FetchAsync(s, q, token).ConfigureAwait(false);
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

        var now = _work.Snapshot();
        status = now.Status;
        inProgress = now.InProgress;
        onCompleted = now.Completed != _seenCompleted;
        _seenCompleted = now.Completed;
        return now.Features;
    }

    static Task<(IReadOnlyList<NtsFeature>?, string)> Done(IReadOnlyList<NtsFeature> features, string status)
        => Task.FromResult<(IReadOnlyList<NtsFeature>?, string)>((features, status));

    static string Host(string server) => Uri.TryCreate(server, UriKind.Absolute, out var u) ? u.Host : server;

    static string Age(TimeSpan age)
        => age.TotalMinutes < 1 ? "seconds"
         : age.TotalHours < 1 ? $"{(int)age.TotalMinutes} min"
         : age.TotalDays < 1 ? $"{(int)age.TotalHours} h"
         : $"{(int)age.TotalDays} days";

    /// <summary>Stops a request in flight.</summary>
    public void Dispose() => _work.Dispose();
}
