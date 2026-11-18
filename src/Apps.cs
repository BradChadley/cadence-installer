using System.Diagnostics;
using Microsoft.Win32;

namespace BradleysApps;

/// <summary>Where Cadence is on this PC.</summary>
public sealed record Installed(string? Version, string Exe, string Folder);

/// <summary>Asks to close Cadence first: shown in amber, not as an error.</summary>
public sealed class NeedsClosing(string words) : Exception(words);

/// <summary>What the card shows while Cadence is fetched and installed.</summary>
public interface IReport
{
    void Downloading(long have, long? total);
    /// <summary>A step with no measure (checking, installing): the line glides.</summary>
    void Step(string words);
}

/// <summary>Cadence: a Velopack installer of its own, run quietly. It installs for this Windows account, shows in Apps, updates itself.</summary>
public sealed class Cadence
{
    public const string Name = "Cadence";
    public const string Description = "A music player for the music you own, with synced lyrics, radio, a phone remote, and Bradley's Music Tool built in.";
    public const string Repo = "BradChadley/Cadence-Releases";
    /// <summary>The file names a release is read by when GitHub's API is busy.</summary>
    static readonly IReadOnlyList<string> FallbackNames = ["Cadence-win-Setup.exe", "SHA256SUMS.txt"];

    public static string Page => $"https://github.com/{Repo}/releases/latest";
    public Installed? Here { get; private set; }
    Task<Release>? release;

    /// <summary>Looks for Cadence on this PC again (after an install, or when the window opens).</summary>
    public void Look()
    {
        try { Here = Detect(); }
        catch (Exception e) { Log.Write($"Cadence: looking for it failed: {e.Message}"); Here = null; }
        Log.Write($"Cadence: {(Here is { } h ? $"on this PC, {h.Version ?? "version unknown"}, {h.Exe}" : "not on this PC")}");
    }

    /// <summary>The newest release, asked for once and kept; asked again after a failure.</summary>
    public Task<Release> Newest(CancellationToken ct)
    {
        if (release is { IsFaulted: true } or { IsCanceled: true }) release = null;
        return release ??= Releases.Latest(Repo, FallbackNames, ct);
    }

    static string? ProductVersion(string exe)
    {
        try { return FileVersionInfo.GetVersionInfo(exe).ProductVersion?.Split('+')[0].Trim() is { Length: > 0 } v ? v : null; }
        catch { return null; }
    }

    static Installed? Detect()
    {
        using var uninstall = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
        foreach (var name in uninstall?.GetSubKeyNames() ?? [])
        {
            using var key = uninstall!.OpenSubKey(name);
            if (key == null) continue;
            var shown = key.GetValue("DisplayName") as string;
            var remove = key.GetValue("UninstallString") as string ?? "";
            if (!(name.Equals("Cadence", StringComparison.OrdinalIgnoreCase) || (shown == "Cadence" && remove.Contains("Update.exe", StringComparison.OrdinalIgnoreCase)))) continue;
            if (key.GetValue("InstallLocation") is not string folder || !Directory.Exists(folder)) continue;
            // an entry in Apps and its folder are enough to count as installed, even with files gone: that is a repair
            var exe = Path.Combine(folder, "Cadence.exe");
            if (!File.Exists(exe)) exe = Path.Combine(folder, "current", "Cadence.exe");
            var version = Velopack(folder).Version ?? key.GetValue("DisplayVersion") as string ?? ProductVersion(Path.Combine(folder, "current", "Cadence.exe"));
            return new Installed(version, exe, folder);
        }
        // installed without an entry in Apps (should not happen with Velopack, but costs nothing to look)
        var standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cadence");
        var current = Path.Combine(standard, "current", "Cadence.exe");
        return File.Exists(current) ? new Installed(Velopack(standard).Version ?? ProductVersion(current), File.Exists(Path.Combine(standard, "Cadence.exe")) ? Path.Combine(standard, "Cadence.exe") : current, standard) : null;
    }

    /// <summary>The version and main program Velopack recorded in current\sq.version when it installed (null if unreadable).</summary>
    static (string? Version, string? MainExe) Velopack(string folder)
    {
        try
        {
            // two plain fields of a small file: read as text (an XML library would add a megabyte to this exe)
            var text = File.ReadAllText(Path.Combine(folder, "current", "sq.version"));
            string? Read(string n)
            {
                var start = text.IndexOf($"<{n}>", StringComparison.Ordinal);
                if (start < 0) return null;
                start += n.Length + 2;
                var end = text.IndexOf('<', start);
                var value = end < 0 ? "" : System.Net.WebUtility.HtmlDecode(text[start..end]).Trim();
                return value.Length > 0 ? value : null;
            }
            return (Read("version"), Read("mainExe"));
        }
        catch { return (null, null); }
    }

    /// <summary>
    /// What is wrong with the copy on this PC, in a few plain words ("some of its files are missing"), or null when it
    /// is whole (or there is none). Only what can be told for sure: a doubt counts as whole, never as damage.
    /// </summary>
    public string? Damage() => Here is { } h ? Health(h.Folder) : null;

    /// <summary>
    /// Checks a Velopack install of Cadence at <paramref name="folder"/>: Velopack's own pieces (Update.exe, the
    /// launcher, current\sq.version, the program it names) and every library Cadence's .deps.json lists. Null when whole.
    /// </summary>
    public static string? Health(string folder)
    {
        var missing = new List<string>();
        void Need(string relative) { if (!File.Exists(Path.Combine(folder, relative))) missing.Add(relative); }
        var current = Path.Combine(folder, "current");
        Need("Update.exe");
        Need(Path.Combine("current", "sq.version"));
        var main = Velopack(folder).MainExe ?? "Cadence.exe";
        Need(main);                                  // the launcher the shortcuts open
        Need(Path.Combine("current", main));         // the program itself
        // every library the program says it loads (only when the list is there: a build without one is not damage)
        var deps = Path.Combine(current, Path.ChangeExtension(main, ".deps.json"));
        if (File.Exists(deps))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(deps));
                var root = doc.RootElement;
                var target = root.GetProperty("runtimeTarget").GetProperty("name").GetString()!;
                foreach (var lib in root.GetProperty("targets").GetProperty(target).EnumerateObject())
                    foreach (var kind in new[] { "runtime", "native" })
                        if (lib.Value.TryGetProperty(kind, out var files))
                            foreach (var f in files.EnumerateObject())
                            {
                                // only the libraries it loads: the list also names debug files and tools a build leaves out on purpose
                                if (!f.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                                var rel = f.Name.Replace('/', Path.DirectorySeparatorChar);
                                if (!File.Exists(Path.Combine(current, rel)) && !File.Exists(Path.Combine(current, Path.GetFileName(rel))))
                                    missing.Add(Path.Combine("current", Path.GetFileName(rel)));
                            }
            }
            catch (Exception e) { Log.Write($"Cadence's library list could not be read ({e.Message}); only its main files were checked"); }
        }
        if (missing.Count == 0) return null;
        Log.Write($"Cadence at {folder} is missing {missing.Count} file(s): {string.Join(", ", missing.Take(12))}{(missing.Count > 12 ? ", ..." : "")}");
        return "some of its files are missing";
    }

    public bool IsRunning()
    {
        if (Here is not { } h) return false;
        var open = false;
        foreach (var p in Process.GetProcessesByName("Cadence"))
        {
            string? path;
            try { path = p.MainModule?.FileName; } catch { path = ""; } // a process we may not read counts, to be safe
            finally { p.Dispose(); }
            open |= path != null && (path.Length == 0 || path.StartsWith(h.Folder, StringComparison.OrdinalIgnoreCase));
        }
        return open;
    }

    /// <summary>Fetches and checks the newest release into <paramref name="work"/>; installs it unless <paramref name="testRun"/>.</summary>
    public async Task<string> Install(string work, IReport report, bool testRun, CancellationToken ct)
    {
        var r = await Newest(ct);
        var setup = r.Find(n => n.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase))
            ?? throw new Problem("The newest Cadence release doesn't have its installer.", retry: false);
        // two checksums when the release has them: GitHub's own, and the SHA256SUMS list published with the release
        string? listed = null;
        if (r.Find(n => n.StartsWith("SHA256SUMS", StringComparison.OrdinalIgnoreCase)) is { } sums && await Net.Text(sums.Url, ct) is { } text)
            listed = Verify.FromSums(text, setup.Name);
        if (setup.Sha256 != null && listed != null && setup.Sha256 != listed)
            throw new Problem("Cadence's two published checksums disagree, so nothing was installed.", retry: false);
        var want = setup.Sha256 ?? listed ?? throw new Problem("Cadence's installer couldn't be checked, so it wasn't used.", retry: false);
        Log.Write($"Cadence {r.Version}: {setup.Name}, SHA-256 {want} (GitHub {setup.Sha256 != null}, list {listed != null})");

        var path = Path.Combine(work, setup.Name);
        await Fetch(setup, path, want, report, ct);
        if (testRun) return r.Version;

        Look();
        if (IsRunning()) throw new NeedsClosing("Close Cadence first, then click Install.");
        report.Step("Installing");
        var log = Path.Combine(work, "cadence-setup.log");
        using var p = Process.Start(new ProcessStartInfo(path) { ArgumentList = { "--silent", "--log", log }, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = work })
            ?? throw new Problem("Cadence's installer didn't start.");
        // from here on it is not stopped halfway: Velopack puts the old copy back itself if it fails
        using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        try { await p.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException) { throw new Problem("Cadence's installer took too long and was left running."); }
        Log.Write($"Cadence's installer ended with {p.ExitCode}");
        if (p.ExitCode != 0)
        {
            try { foreach (var line in File.ReadLines(log).TakeLast(30)) Log.Write("  setup: " + line); } catch { }
            throw new Problem("Cadence's installer stopped with an error.");
        }
        Look();
        if (Here == null) throw new Problem("Cadence's installer finished, but Cadence isn't there.");
        // the same check as a damaged install: only a whole Cadence counts as installed
        if (Health(Here.Folder) is { } wrong) throw new Problem($"Cadence's installer finished, but {wrong}.");
        return Here.Version ?? r.Version;
    }

    /// <summary>Fetches a file and checks it against <paramref name="sha256"/>; a mismatch is fetched once more from scratch.</summary>
    static async Task Fetch(Asset a, string path, string sha256, IReport report, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            await Net.Download(a.Url, path, a.Size, report.Downloading, ct);
            report.Step("Checking");
            var got = await Verify.Sha256(path, ct);
            if (got == sha256) { Log.Write($"{a.Name}: SHA-256 matches ({got})"); return; }
            Log.Write($"{a.Name}: SHA-256 {got}, expected {sha256} (try {attempt})");
            File.Delete(path);
            if (attempt >= 2) throw new Problem("The download didn't match its checksum, so it wasn't used.");
        }
    }
}
