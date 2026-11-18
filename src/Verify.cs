using System.Security.Cryptography;

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
