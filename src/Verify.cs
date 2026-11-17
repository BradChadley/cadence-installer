using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BradleysApps;

public static class Verify
{
    public static async Task<string> Sha256(string path, CancellationToken ct)
    {
        await using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(f, ct));
    }

    public static bool IsSha256(string? s) => s is { Length: 64 } && s.All(Uri.IsHexDigit);

    /// <summary>The checksum a SHA256SUMS list gives for <paramref name="name"/> ("hash  name" or "hash *name" lines).</summary>
    public static string? FromSums(string sums, string name)
    {
        foreach (var raw in sums.Split('\n'))
        {
            var line = raw.Trim();
            var gap = line.IndexOfAny([' ', '\t']);
            if (gap != 64) continue;
            var hash = line[..64];
            var file = line[64..].TrimStart(' ', '\t').TrimStart('*').Trim();
            if (IsSha256(hash) && string.Equals(Path.GetFileName(file.Replace('\\', '/')), name, StringComparison.OrdinalIgnoreCase)) return hash.ToLowerInvariant();
        }
        return null;
    }
}

/// <summary>
/// The Music Tool's update.json: the version, size and SHA-256 of bradleys-music-tool.exe, signed with Bradley's key
/// (ECDSA P-256). The app's own updater checks the same signature with the same public key (src\update.rs in the Music Tool's source).
/// </summary>
public sealed record Manifest(string Version, long Size, string Sha256)
{
    /// <summary>The public half of the key the Music Tool's releases are signed with (PUBLIC_KEY in src\update.rs of the Music Tool's source).</summary>
    public const string PublicKey = "65a23585fb97bae6b74c5d67d997da82c962b6b6a699962288a2e0bf75efc6b7abd3123e3c8d8e6a05154860c9344c977e2f10a0cbb993065ba46f58bd8b1685";

    /// <summary>The manifest when it is well formed and signed by <paramref name="key"/>; null otherwise.</summary>
    public static Manifest? ReadSigned(string json, string key = PublicKey)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var version = r.GetProperty("version").GetString() ?? "";
            var size = r.GetProperty("size").GetInt64();
            var sha = r.GetProperty("sha256").GetString() ?? "";
            var sig = r.GetProperty("signature").GetString() ?? "";
            if (version.Split('.').Length != 3 || size <= 0 || size > 300L * 1024 * 1024 || !Verify.IsSha256(sha) || sig.Length != 128 || !sig.All(Uri.IsHexDigit)) return null;
            if (sha != sha.ToLowerInvariant()) return null;
            var k = Convert.FromHexString(key);
            using var ec = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = k[..32], Y = k[32..] } });
            var text = Encoding.UTF8.GetBytes($"bradleys-music-tool update\n{version}\n{size}\n{sha}\n");
            return ec.VerifyData(text, Convert.FromHexString(sig), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
                ? new Manifest(version, size, sha) : null;
        }
        catch (Exception e) // anything at all that goes wrong here means "not signed", never a crash
        {
            Log.Write($"update.json could not be read: {e.Message}");
            return null;
        }
    }
}

/// <summary>
/// Which ffmpeg the Music Tool uses: the version, where its makers publish it, its size and SHA-256. Read from the
/// Music Tool release's helpers.json, signed with the same key as update.json (so it follows the app's own pin in
/// src\helpers.rs with no new build here), or the pin below when a release has none.
/// Signed text, UTF-8, each line ended by a line feed:
///   bradleys-music-tool helpers
///   ffmpeg
///   &lt;version&gt;
///   &lt;url&gt;
///   &lt;size in bytes&gt;
///   &lt;SHA-256 of the zip, 64 small hex letters&gt;
/// helpers.json: {"ffmpeg": {"version": "...", "url": "...", "size": ..., "sha256": "...", "signature": "&lt;r and s, 128 hex&gt;"}}
/// </summary>
public sealed record HelperPin(string Version, string Url, long Size, string Sha256)
{
    /// <summary>The app's pin when this was built (src\helpers.rs FFMPEG): the start, until releases carry helpers.json.</summary>
    public static readonly HelperPin BuiltIn = new("9.0.2", "https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip",
        114_768_076, "60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba");

    public static string SignedText(string version, string url, long size, string sha256) =>
        $"bradleys-music-tool helpers\nffmpeg\n{version}\n{url}\n{size}\n{sha256}\n";

    /// <summary>The ffmpeg pin in <paramref name="json"/> when it is well formed and signed by <paramref name="key"/>; null otherwise.</summary>
    public static HelperPin? ReadSigned(string json, string key = Manifest.PublicKey)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var f = doc.RootElement.GetProperty("ffmpeg");
            var version = f.GetProperty("version").GetString() ?? "";
            var url = f.GetProperty("url").GetString() ?? "";
            var size = f.GetProperty("size").GetInt64();
            var sha = f.GetProperty("sha256").GetString() ?? "";
            var sig = f.GetProperty("signature").GetString() ?? "";
            if (version.Length is 0 or > 40 || size <= 0 || size > 1024L * 1024 * 1024 || !Verify.IsSha256(sha) || sha != sha.ToLowerInvariant() || sig.Length != 128) return null;
            // only an https download, and never this PC or a private address
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps || u.IsLoopback || u.HostNameType != UriHostNameType.Dns) return null;
            var k = Convert.FromHexString(key);
            using var ec = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = k[..32], Y = k[32..] } });
            return ec.VerifyData(Encoding.UTF8.GetBytes(SignedText(version, url, size, sha)), Convert.FromHexString(sig), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
                ? new HelperPin(version, url, size, sha) : null;
        }
        catch (Exception e) // anything at all that goes wrong here means "not signed", never a crash
        {
            Log.Write($"helpers.json could not be read: {e.Message}");
            return null;
        }
    }
}
