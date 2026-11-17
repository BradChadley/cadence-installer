using System.Text.Json;

namespace BradleysApps;

/// <summary>One file on a release: where it is, how big, and the SHA-256 GitHub worked out when it was uploaded.</summary>
public sealed record Asset(string Name, string Url, long? Size, string? Sha256);

/// <summary>The newest release of an app, read from GitHub's API, or (when the API is busy) from github.com itself.</summary>
public sealed record Release(string Version, string Page, IReadOnlyList<Asset> Assets, bool FromApi)
{
    public Asset? Find(Func<string, bool> name) => Assets.FirstOrDefault(a => name(a.Name));
}

public static class Releases
{
    /// <summary>For testing the way without the API (--check --no-api).</summary>
    public static bool ForceFallback;

    /// <summary>
    /// The newest published release of <paramref name="repo"/>. GitHub's API answers 60 questions an hour for each
    /// internet address; past that (or if the API is down) the release is read from github.com's own "latest" links,
    /// with the file names <paramref name="fallbackNames"/> (no GitHub checksums then: each app's own check still holds).
    /// </summary>
    public static async Task<Release> Latest(string repo, IReadOnlyList<string> fallbackNames, CancellationToken ct)
    {
        var page = $"https://github.com/{repo}/releases/latest";
        string? json = null;
        if (!ForceFallback) try { json = await Net.Text($"https://api.github.com/repos/{repo}/releases/latest", ct, "application/vnd.github+json"); }
        catch (Problem) { /* the way below says the same if github.com cannot be reached either */ }
        if (json != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var tag = root.GetProperty("tag_name").GetString() ?? "";
                var assets = new List<Asset>();
                foreach (var a in root.GetProperty("assets").EnumerateArray())
                {
                    if (a.TryGetProperty("state", out var st) && st.GetString() != "uploaded") continue;
                    var digest = a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                    var sha = digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..].ToLowerInvariant() : null;
                    assets.Add(new Asset(a.GetProperty("name").GetString() ?? "", a.GetProperty("browser_download_url").GetString() ?? "", a.GetProperty("size").GetInt64(), sha));
                }
                var html = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? page : page;
                Log.Write($"{repo}: {tag} from the API, {assets.Count} files");
                return new Release(Plain(tag), html, assets, true);
            }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                Log.Write($"{repo}: the API's answer could not be read: {e.Message}");
            }
        }
        var latest = await Net.LatestTag(repo, ct) ?? throw new Problem("Couldn't reach GitHub. Check your internet connection.", page: false);
        Log.Write($"{repo}: {latest} from github.com (the API did not answer)");
        var files = fallbackNames.Select(n => new Asset(n, $"https://github.com/{repo}/releases/download/{Uri.EscapeDataString(latest)}/{Uri.EscapeDataString(n)}", null, null)).ToList();
        return new Release(Plain(latest), $"https://github.com/{repo}/releases/tag/{Uri.EscapeDataString(latest)}", files, false);
    }

    static string Plain(string tag) => tag.TrimStart('v', 'V');

    /// <summary>"1.0.10" is newer than "1.0.9": compares the numbers, not the text.</summary>
    public static int Compare(string? a, string? b)
    {
        static int[] Parts(string? v) => (v ?? "").Split('-', '+')[0].Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        var x = Parts(a); var y = Parts(b);
        for (var i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            var c = (i < x.Length ? x[i] : 0).CompareTo(i < y.Length ? y[i] : 0);
            if (c != 0) return c;
        }
        return 0;
    }
}
