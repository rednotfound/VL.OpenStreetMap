using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VL.OpenStreetMap;

/// <summary>A failed Overpass request, with a message a user can act on.</summary>
internal sealed class OverpassException(string message) : Exception(message);

/// <summary>
/// Talks to an Overpass server politely and keeps every answer on disk. Internal.
/// </summary>
/// <remarks>
/// <para>The usage policy (https://wiki.openstreetmap.org/wiki/Overpass_API): identify yourself,
/// never run requests in parallel, wait 30 s after a 429, use planet extracts for big areas. And the
/// main server is busy: measured 2026-11-15, the first request got 502 "at capacity", the next took
/// 33 s. So one request at a time in the whole process, a User-Agent naming the package, and every
/// answer cached so a patch that worked once keeps working offline.</para>
/// </remarks>
internal static class OverpassClient
{
    internal const string DefaultServer = "https://overpass-api.de/api/interpreter";

    internal static readonly string UserAgent =
        $"VL.OpenStreetMap/{typeof(OverpassClient).Assembly.GetName().Version?.ToString(3)} (+https://github.com/rednotfound/VL.OpenStreetMap)";

    static readonly HttpClient Http = CreateHttp();
    static readonly SemaphoreSlim OneAtATime = new(1, 1);
    static DateTime _notBefore = DateTime.MinValue;

    static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return http;
    }

    /// <summary>Where answers are kept; tests point it elsewhere.</summary>
    internal static string CacheDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VL.OpenStreetMap", "overpass");

    internal static string CacheFile(string server, string query)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(server.Trim() + "\n" + query.Trim()));
        return Path.Combine(CacheDirectory, Convert.ToHexString(hash)[..32] + ".osm");
    }

    /// <summary>The cached answer to this query on this server, and how old it is, if there is one.</summary>
    internal static bool TryCached(string server, string query, out string xml, out TimeSpan age)
    {
        var file = CacheFile(server, query);
        if (File.Exists(file))
        {
            xml = File.ReadAllText(file);
            age = DateTime.UtcNow - File.GetLastWriteTimeUtc(file);
            return true;
        }
        xml = "";
        age = TimeSpan.Zero;
        return false;
    }

    /// <summary>Asks the server, waits its turn, caches the answer. Throws <see cref="OverpassException"/>.</summary>
    internal static async Task<string> FetchAsync(string server, string query, CancellationToken token)
    {
        await OneAtATime.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var wait = _notBefore - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
                throw new OverpassException($"the server asked to wait: try again in {Math.Ceiling(wait.TotalSeconds)} s");

            HttpResponseMessage response;
            try
            {
                using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("data", query) });
                response = await Http.PostAsync(server, content, token).ConfigureAwait(false);
            }
            catch (HttpRequestException e)
            {
                throw new OverpassException($"no answer from {new Uri(server).Host} ({e.Message.TrimEnd('.')}): offline, or try another Server");
            }
            catch (TaskCanceledException) when (!token.IsCancellationRequested)
            {
                throw new OverpassException("no answer within 3 minutes: shrink the area, or try another Server");
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                switch (response.StatusCode)
                {
                    case HttpStatusCode.OK:
                        if (!body.Contains("<osm", StringComparison.Ordinal))
                            throw new OverpassException("the answer is not OSM XML: the query must ask for [out:xml]");
                        if (body.Contains("<remark>", StringComparison.Ordinal) && body.Contains("runtime error", StringComparison.Ordinal))
                            throw new OverpassException("the server stopped the query (timeout or memory): shrink the area or raise [timeout:]");
                        Directory.CreateDirectory(CacheDirectory);
                        File.WriteAllText(CacheFile(server, query), body);
                        return body;
                    case HttpStatusCode.BadRequest:
                        throw new OverpassException("the query has an error: " + FirstError(body));
                    case HttpStatusCode.TooManyRequests:
                        _notBefore = DateTime.UtcNow.AddSeconds(30);
                        throw new OverpassException("too many requests: wait 30 s (the server's rule)");
                    case HttpStatusCode.GatewayTimeout:
                        throw new OverpassException("too large, or the server is busy: shrink the area, or try again later");
                    case HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable:
                        throw new OverpassException("the server is at capacity: try again in a minute, or another Server");
                    default:
                        throw new OverpassException($"the server answered {(int)response.StatusCode} {response.ReasonPhrase}");
                }
            }
        }
        finally
        {
            OneAtATime.Release();
        }
    }

    // Overpass explains a bad query in its HTML body: "Error: line 2: parse error: ..."
    static string FirstError(string body)
    {
        var i = body.IndexOf("Error:", StringComparison.Ordinal);
        if (i < 0) return "see the Overpass documentation";
        var end = body.IndexOf('<', i);
        return WebUtility.HtmlDecode(body[i..(end < 0 ? Math.Min(body.Length, i + 200) : end)]).Trim();
    }
}
