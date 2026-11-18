using System.Net;
using System.Net.Http.Headers;

namespace BradleysApps;

/// <summary>A plain-words failure the card can show as it is.</summary>
public sealed class Problem(string words, bool retry = true, bool page = true, Exception? inner = null) : Exception(words, inner)
{
    /// <summary>"Try again" is worth offering (a dropped connection), not for a file that failed its check twice.</summary>
    public bool Retry { get; } = retry;
    /// <summary>"Download page" is worth offering: the person can still get the app by hand.</summary>
    public bool Page { get; } = page;
}

/// <summary>Everything that goes over the network: GitHub's API, the release files, with resume and retries.</summary>
public static class Net
{
    public const string UserAgent = "CadenceInstaller/1.1 (+https://github.com/BradChadley)";
    static readonly TimeSpan Stall = TimeSpan.FromSeconds(30);

    static HttpClient Make(bool redirects) => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = redirects,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(20),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
        DefaultRequestHeaders = { { "User-Agent", UserAgent } },
    };

    public static readonly HttpClient Http = Make(true);
    /// <summary>Reads where github.com points "latest" without following it (the tag is in the address).</summary>
    public static readonly HttpClient NoRedirect = Make(false);

    /// <summary>A small text file (a JSON answer, a checksum list), with a time limit and a size limit.</summary>
    public static async Task<string?> Text(string url, CancellationToken ct, string? accept = null, int most = 2_000_000)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (accept != null) req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if (res.StatusCode == HttpStatusCode.NotFound) { Log.Write($"GET {url}: 404"); return null; }
                if (!res.IsSuccessStatusCode)
                {
                    Log.Write($"GET {url}: {(int)res.StatusCode} {res.ReasonPhrase}");
                    // 403/429 from the API is its hourly limit: the caller has another way, so no retry here
                    if (res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests || attempt >= 3) return null;
                }
                else
                {
                    if (res.Content.Headers.ContentLength > most) { Log.Write($"GET {url}: too large"); return null; }
                    var text = await res.Content.ReadAsStringAsync(cts.Token);
                    return text.Length > most ? null : text;
                }
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                Log.Write($"GET {url}: {e.GetType().Name}: {e.Message}");
                if (attempt >= 3) throw new Problem("Couldn't reach GitHub. Check your internet connection.", inner: e);
            }
            await Task.Delay(TimeSpan.FromSeconds(attempt * 1.5), ct);
        }
    }

    /// <summary>The tag github.com's "latest" points at (for example "v0.26.8"), read without the API.</summary>
    public static async Task<string?> LatestTag(string repo, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var res = await NoRedirect.GetAsync($"https://github.com/{repo}/releases/latest", HttpCompletionOption.ResponseHeadersRead, cts.Token);
            var to = res.Headers.Location?.ToString();
            var at = to?.LastIndexOf("/tag/", StringComparison.Ordinal) ?? -1;
            return at < 0 ? null : Uri.UnescapeDataString(to![(at + 5)..]);
        }
        catch (Exception e) when (!ct.IsCancellationRequested) { Log.Write($"latest tag of {repo}: {e.Message}"); return null; }
    }

    /// <summary>
    /// Fetches <paramref name="url"/> into <paramref name="path"/>. A dropped connection carries on where it stopped (up to
    /// five tries); a connection that sends nothing for 30 seconds counts as dropped. <paramref name="progress"/> gets
    /// (bytes so far, bytes in all when known).
    /// </summary>
    public static async Task Download(string url, string path, long? size, Action<long, long?> progress, CancellationToken ct)
    {
        var buffer = new byte[256 * 1024];
        for (var attempt = 1; ; attempt++)
        {
            long have = File.Exists(path) ? new FileInfo(path).Length : 0;
            if (size is { } s && have > s) { File.Delete(path); have = 0; }
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                if (have > 0) req.Headers.Range = new RangeHeaderValue(have, null);
                using var headers = CancellationTokenSource.CreateLinkedTokenSource(ct);
                headers.CancelAfter(TimeSpan.FromSeconds(45));
                using var res = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, headers.Token);
                if (res.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && size is { } whole && have == whole) { progress(have, whole); return; }
                if (res.StatusCode == HttpStatusCode.NotFound) throw new Problem("The newest release is missing a file it should have.", retry: false);
                res.EnsureSuccessStatusCode();
                var resumed = res.StatusCode == HttpStatusCode.PartialContent;
                if (!resumed) have = 0;
                long? total = size ?? (res.Content.Headers.ContentLength is { } len ? len + have : null);
                await using var body = await res.Content.ReadAsStreamAsync(ct);
                await using var file = new FileStream(path, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.Asynchronous);
                progress(have, total);
                var shown = Environment.TickCount64;
                while (true)
                {
                    using var read = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    read.CancelAfter(Stall);
                    int n;
                    try { n = await body.ReadAsync(buffer, read.Token); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new IOException("nothing arrived for 30 seconds"); }
                    if (n == 0) break;
                    await file.WriteAsync(buffer.AsMemory(0, n), ct);
                    have += n;
                    if (size is { } most && have > most) throw new Problem("The download was larger than the release says, so it wasn't used.", retry: false);
                    // about 30 updates a second is plenty for a smooth line
                    if (Environment.TickCount64 - shown >= 33) { progress(have, total); shown = Environment.TickCount64; }
                }
                progress(have, total);
                if (total is { } t && have != t) throw new IOException($"the download ended at {have} of {t} bytes");
                return;
            }
            catch (Exception e) when (e is not Problem && !ct.IsCancellationRequested)
            {
                Log.Write($"download {url}, try {attempt}: {e.GetType().Name}: {e.Message}");
                if (attempt >= 5) throw new Problem("The connection dropped.", inner: e);
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(10, attempt * 2)), ct);
            }
        }
    }
}

/// <summary>A plain log beside the downloads, for when something goes wrong on someone else's PC.</summary>
public static class Log
{
    static readonly object Gate = new();
    public static string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Cadence Installer.log");

    public static void Start()
    {
        try
        {
            // one run's log, so it never grows without end
            File.WriteAllText(Path, $"Cadence Installer {typeof(Log).Assembly.GetName().Version}, {DateTime.Now:yyyy-MM-dd HH:mm:ss}, {Environment.OSVersion.VersionString}{Environment.NewLine}");
        }
        catch { }
    }

    public static void Write(string line)
    {
        lock (Gate)
        {
            try { File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff}  {line}{Environment.NewLine}"); } catch { }
        }
    }
}
