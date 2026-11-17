using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Win32;

namespace BradleysApps;

/// <summary>Where an app is on this PC.</summary>
public sealed record Installed(string? Version, string Exe, string Folder);

/// <summary>Asks to close an app first: shown in amber, not as an error.</summary>
public sealed class NeedsClosing(string words) : Exception(words);

/// <summary>What a card shows while its app is fetched and installed.</summary>
public interface IReport
{
    void Downloading(long have, long? total);
    /// <summary>A step with no measure (checking, installing): the line glides.</summary>
    void Step(string words);
    /// <summary>The word the download line starts with from now on ("Downloading ffmpeg").</summary>
    void Label(string words) { }
}

public abstract class App
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string Repo { get; }
    public abstract string Picture { get; }
    /// <summary>The file names a release is read by when GitHub's API is busy.</summary>
    protected abstract IReadOnlyList<string> FallbackNames { get; }
    /// <summary>How the app's name reads inside a sentence ("Close Cadence first").</summary>
    public virtual string ShortName => Name;

    public string Page => $"https://github.com/{Repo}/releases/latest";
    public Installed? Here { get; private set; }
    /// <summary>A sentence for the end of the last install, beside the version ("ffmpeg is ready too."), or null.</summary>
    public string? Note { get; protected set; }
    Task<Release>? release;

    /// <summary>Looks for the app on this PC again (after an install, or when the window opens).</summary>
    public void Look()
    {
        try { Here = Detect(); }
        catch (Exception e) { Log.Write($"{Name}: looking for it failed: {e.Message}"); Here = null; }
        Log.Write($"{Name}: {(Here is { } h ? $"on this PC, {h.Version ?? "version unknown"}, {h.Exe}" : "not on this PC")}");
    }

    /// <summary>The newest release, asked for once and kept; asked again after a failure.</summary>
    public Task<Release> Newest(CancellationToken ct)
    {
        if (release is { IsFaulted: true } or { IsCanceled: true }) release = null;
        return release ??= Releases.Latest(Repo, FallbackNames, ct);
    }

    protected abstract Installed? Detect();
    public abstract bool IsRunning();

    /// <summary>
    /// What is wrong with the copy on this PC, in a few plain words ("some of its files are missing"), or null when it
    /// is whole (or there is none). Only what can be told for sure: a doubt counts as whole, never as damage.
    /// </summary>
    public virtual Task<string?> Damage(CancellationToken ct) => Task.FromResult<string?>(null);

    /// <summary>Fetches and checks the newest release into <paramref name="work"/>; installs it unless <paramref name="testRun"/>.</summary>
    public abstract Task<string> Install(string work, IReport report, bool testRun, CancellationToken ct);

    /// <summary>One install at a time: two installers never run side by side.</summary>
    protected static readonly SemaphoreSlim Gate = new(1, 1);

    protected static string? ProductVersion(string exe)
    {
        try { return FileVersionInfo.GetVersionInfo(exe).ProductVersion?.Split('+')[0].Trim() is { Length: > 0 } v ? v : null; }
        catch { return null; }
    }

    protected static IEnumerable<string> RunningPaths(string processName)
    {
        foreach (var p in Process.GetProcessesByName(processName))
        {
            string? path = null;
            try { path = p.MainModule?.FileName; } catch { path = ""; } // a process we may not read is reported, to be safe
            finally { p.Dispose(); }
            if (path != null) yield return path;
        }
    }

    /// <summary>Fetches a file and checks it against <paramref name="sha256"/>; a mismatch is fetched once more from scratch.</summary>
    protected static async Task Fetch(Asset a, string path, string? sha256, IReport report, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            await Net.Download(a.Url, path, a.Size, report.Downloading, ct);
            if (sha256 == null) return;
            report.Step("Checking");
            var got = await Verify.Sha256(path, ct);
            if (got == sha256) { Log.Write($"{a.Name}: SHA-256 matches ({got})"); return; }
            Log.Write($"{a.Name}: SHA-256 {got}, expected {sha256} (try {attempt})");
            File.Delete(path);
            if (attempt >= 2) throw new Problem("The download didn't match its checksum, so it wasn't used.");
        }
    }
}

/// <summary>Cadence: a Velopack installer of its own, run quietly. It installs for this Windows account, shows in Apps, updates itself.</summary>
public sealed class Cadence : App
{
    public override string Name => "Cadence";
    public override string Description => "A music player for the music you own, with synced lyrics, smart playlists, radio and a phone remote.";
    public override string Repo => "BradChadley/Cadence-Releases";
    public override string Picture => "cadence.png";
    protected override IReadOnlyList<string> FallbackNames => ["Cadence-win-Setup.exe", "SHA256SUMS.txt"];

    protected override Installed? Detect()
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

    public override Task<string?> Damage(CancellationToken ct) =>
        Task.FromResult(Here is { } h ? Health(h.Folder) : null);

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

    public override bool IsRunning() =>
        Here is { } h && RunningPaths("Cadence").Any(p => p.Length == 0 || p.StartsWith(h.Folder, StringComparison.OrdinalIgnoreCase));

    public override async Task<string> Install(string work, IReport report, bool testRun, CancellationToken ct)
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

        report.Step("Waiting");
        await Gate.WaitAsync(ct);
        try
        {
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
            return Here.Version ?? r.Version;
        }
        finally { Gate.Release(); }
    }
}

/// <summary>
/// Bradley's Music Tool: one exe that replaces itself to update, published as a zip with no installer. This puts it
/// where its own ship script does (or updates the copy already here), with a Start menu and a desktop shortcut.
/// </summary>
public sealed class MusicTool : App
{
    const string ExeName = "bradleys-music-tool.exe";
    public override string Name => "Bradley's Music Tool";
    public override string ShortName => "the Music Tool";
    public override string Description => "Recommends music from what you listen to, downloads and tags whole albums, and repairs your library.";
    public override string Repo => "BradChadley/bradleys-music-tool";
    public override string Picture => "music-tool.png";
    protected override IReadOnlyList<string> FallbackNames => ["update.json", ExeName];

    static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static string StandardFolder => Path.Combine(LocalAppData, "Programs", "Bradley's Music Tool");
    static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Bradley's Music Tool.lnk");
    static string DesktopFolder => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    static bool Usable(string? exe) =>
        exe != null && Path.GetFileName(exe).Equals(ExeName, StringComparison.OrdinalIgnoreCase) && File.Exists(exe)
        // a build in a cargo target folder is someone's work in progress, not an install
        && !exe.Contains(@"\target\", StringComparison.OrdinalIgnoreCase);

    static IEnumerable<string> Links()
    {
        var places = new[]
        {
            (DesktopFolder, false),
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), false),
            (Environment.GetFolderPath(Environment.SpecialFolder.Programs), true),
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"), false),
        };
        foreach (var (dir, deep) in places)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, "*.lnk", new EnumerationOptions { RecurseSubdirectories = deep, MaxRecursionDepth = 2, IgnoreInaccessible = true }).ToList(); }
            catch { continue; }
            foreach (var f in files) yield return f;
        }
    }

    protected override Installed? Detect()
    {
        // where it runs from, then where the ship script and this chooser put it, then wherever a shortcut points
        var exe = RunningPaths(Path.GetFileNameWithoutExtension(ExeName)).FirstOrDefault(Usable);
        if (exe == null && Usable(Path.Combine(StandardFolder, ExeName))) exe = Path.Combine(StandardFolder, ExeName);
        exe ??= Links().Select(ShellLink.Target).FirstOrDefault(Usable);
        return exe == null ? null : new Installed(ProductVersion(exe), Path.GetFullPath(exe), Path.GetDirectoryName(Path.GetFullPath(exe))!);
    }

    public override bool IsRunning() =>
        RunningPaths(Path.GetFileNameWithoutExtension(ExeName)).Any(p => p.Length == 0 || (Here != null && p.Equals(Here.Exe, StringComparison.OrdinalIgnoreCase)));

    Task<Manifest>? signed;

    /// <summary>The newest release's signed file list (update.json), read once and kept; read again after a failure.</summary>
    Task<Manifest> Signed(CancellationToken ct)
    {
        if (signed is { IsFaulted: true } or { IsCanceled: true }) signed = null;
        return signed ??= Read();
        async Task<Manifest> Read()
        {
            var r = await Newest(ct);
            var listAsset = r.Find(n => n.Equals("update.json", StringComparison.OrdinalIgnoreCase))
                ?? throw new Problem("The newest Music Tool release doesn't have its signed file list.", retry: false);
            var listText = await Net.Text(listAsset.Url, ct, most: 64 * 1024) ?? throw new Problem("Couldn't reach GitHub. Check your internet connection.");
            var m = Manifest.ReadSigned(listText) ?? throw new Problem("The Music Tool download couldn't be confirmed as Bradley's, so it wasn't used.", retry: false);
            Log.Write($"Music Tool {m.Version}: signed, {m.Size} bytes, SHA-256 {m.Sha256}");
            return m;
        }
    }

    public override async Task<string?> Damage(CancellationToken ct)
    {
        if (Here is not { } h) return null;
        Manifest m;
        try { m = await Signed(ct); }
        catch (Exception e) when (e is not OperationCanceledException) { return null; } // cannot tell, so it is not called damaged
        return await Health(h.Exe, m, ct);
    }

    /// <summary>
    /// Whether the copy at <paramref name="exe"/> is damaged. Only what is sure counts: the file gone, too small to be the
    /// program, its version unreadable, or exactly the signed file's size with other contents (a file spoiled in place).
    /// Another size under the same version is someone's own build (the author ships builds between releases), not damage.
    /// </summary>
    public static async Task<string?> Health(string exe, Manifest m, CancellationToken ct)
    {
        if (!File.Exists(exe)) return "its program file is missing";
        var size = new FileInfo(exe).Length;
        var version = ProductVersion(exe);
        if (size < 1024 * 1024 || version == null)
        {
            Log.Write($"Music Tool at {exe}: {size} bytes, version {(version ?? "unreadable")}");
            return "its program file is damaged";
        }
        if (version != m.Version || size != m.Size || await Matches(exe, m, ct))
        {
            if (version == m.Version && size != m.Size) Log.Write($"Music Tool at {exe} says {m.Version} at {size} bytes, not the released {m.Size}: taken as a build of its own");
            return null;
        }
        Log.Write($"Music Tool at {exe} is the released size but not the signed file (SHA-256 {await Verify.Sha256(exe, ct)})");
        return "its program file is damaged";
    }

    public override async Task<string> Install(string work, IReport report, bool testRun, CancellationToken ct)
    {
        var r = await Newest(ct);
        // the signed file list first: nothing is fetched for an exe that could not be trusted anyway
        var m = await Signed(ct);

        var exe = Path.Combine(work, "music-tool", ExeName);
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        var extras = new List<string>();
        // the zip is the smallest (about 9 MB against 24) and holds the read-me and license; the bare exe is the way back
        if (r.Find(n => n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) is { } zipAsset)
        {
            try
            {
                var zip = Path.Combine(work, "music-tool.zip");
                await Fetch(zipAsset, zip, zipAsset.Sha256, report, ct);
                report.Step("Checking");
                using var archive = ZipFile.OpenRead(zip);
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith('/') || entry.Length > m.Size + 50_000_000) continue;
                    var name = Path.GetFileName(entry.FullName);
                    if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
                    var to = name.Equals(ExeName, StringComparison.OrdinalIgnoreCase) ? exe : Path.Combine(work, "music-tool", name);
                    entry.ExtractToFile(to, true);
                    if (to != exe) extras.Add(to);
                }
            }
            catch (Exception e) when (e is InvalidDataException or IOException && !ct.IsCancellationRequested)
            {
                Log.Write($"the zip could not be used: {e.Message}");
            }
        }
        if (!await Matches(exe, m, ct))
        {
            var bare = r.Find(n => n.Equals(ExeName, StringComparison.OrdinalIgnoreCase))
                ?? throw new Problem("The Music Tool download couldn't be confirmed as Bradley's, so it wasn't used.", retry: false);
            if (File.Exists(exe)) File.Delete(exe);
            await Fetch(bare, exe, m.Sha256, report, ct);
            if (!await Matches(exe, m, ct)) throw new Problem("The Music Tool download couldn't be confirmed as Bradley's, so it wasn't used.", retry: false);
        }
        Log.Write("Music Tool: the exe matches the signed file list");

        // ffmpeg, so the app can check and convert music from its first start (skipped when this PC already has it).
        // It comes from its makers' own release, checked against the app's pin; a failure here never stops the app.
        Note = null;
        string? ffmpeg = null;
        if (ForceFfmpeg || FfmpegMissing())
        {
            try
            {
                var pin = await FfmpegPin(ct);
                report.Label("Downloading ffmpeg");
                ffmpeg = await FetchFfmpeg(pin, work, report, ct);
            }
            catch (Exception e) when ((e is Problem or IOException or InvalidDataException or UnauthorizedAccessException) && !ct.IsCancellationRequested)
            {
                Log.Write($"ffmpeg: {e.Message}");
                Note = "ffmpeg didn't download; the app will offer to get it.";
            }
            finally { report.Label("Downloading"); }
        }
        if (testRun) return m.Version;

        report.Step("Waiting");
        await Gate.WaitAsync(ct);
        try
        {
            Look();
            if (IsRunning()) throw new NeedsClosing("Close the Music Tool first, then click Install.");
            report.Step("Installing");
            var folder = Here?.Folder is { } f && Writable(f) ? f : StandardFolder;
            var final = Place(exe, folder);
            foreach (var x in extras)
            {
                try { File.Copy(x, Path.Combine(folder, Path.GetFileName(x)), true); }
                catch (Exception e) { Log.Write($"{Path.GetFileName(x)} not copied: {e.Message}"); }
            }
            Shortcuts(final, StartMenuLink, DesktopFolder);
            if (ffmpeg != null)
            {
                try { PlaceFfmpeg(ffmpeg, DataFolder); Note = "ffmpeg is ready too."; }
                catch (Exception e) { Log.Write($"ffmpeg not put in place: {e.Message}"); Note = "ffmpeg couldn't be put in place; the app will offer to get it."; }
            }
            Look();
            return ProductVersion(final) ?? m.Version;
        }
        finally { Gate.Release(); }
    }

    // ---------------------------------------------------------------- ffmpeg

    /// <summary>For --check: fetch ffmpeg even when this PC has it, to test the way there.</summary>
    public static bool ForceFfmpeg;

    /// <summary>The Music Tool's data folder (src\config.rs app_dir), where it keeps the ffmpeg it uses.</summary>
    public static string DataFolder => Path.Combine(LocalAppData, "music-recs");

    /// <summary>
    /// Where the app finds a program of ffmpeg's, in its own order (src\importer\tools.rs look_for_tool): PATH, then
    /// its data folder's tools\ffmpeg, then winget's packages. Null when it would not find it.
    /// </summary>
    public static string? FindTool(string name, string dataFolder)
    {
        var exe = name + ".exe";
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try { var p = Path.Combine(dir.Trim('"'), exe); if (File.Exists(p)) return p; } catch { }
        }
        var own = Path.Combine(dataFolder, "tools", "ffmpeg", exe);
        if (File.Exists(own)) return own;
        var winget = Path.Combine(LocalAppData, "Microsoft", "WinGet", "Packages");
        if (!Directory.Exists(winget)) return null;
        try
        {
            return Directory.EnumerateFiles(winget, exe, new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 6, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive }).FirstOrDefault();
        }
        catch { return null; }
    }

    /// <summary>True when the app would not find ffmpeg or ffprobe on this PC.</summary>
    public static bool FfmpegMissing() => FindTool("ffmpeg", DataFolder) == null || FindTool("ffprobe", DataFolder) == null;

    Task<HelperPin>? ffmpegPin;

    /// <summary>The ffmpeg the newest release asks for (its signed helpers.json), or the built-in pin when it has none or it fails.</summary>
    public Task<HelperPin> FfmpegPin(CancellationToken ct)
    {
        if (ffmpegPin is { IsFaulted: true } or { IsCanceled: true }) ffmpegPin = null;
        return ffmpegPin ??= Read();
        async Task<HelperPin> Read()
        {
            var r = await Newest(ct);
            if (r.Find(n => n.Equals("helpers.json", StringComparison.OrdinalIgnoreCase)) is { } a)
            {
                string? text = null;
                try { text = await Net.Text(a.Url, ct, most: 64 * 1024); } catch (Problem) { }
                if (text != null && HelperPin.ReadSigned(text) is { } signed) { Log.Write($"ffmpeg {signed.Version} from the release's signed helpers.json"); return signed; }
                Log.Write("the release's helpers.json could not be read or is not signed: the built-in ffmpeg pin is used");
            }
            else Log.Write($"the release has no helpers.json: the built-in ffmpeg pin ({HelperPin.BuiltIn.Version}) is used");
            return HelperPin.BuiltIn;
        }
    }

    /// <summary>Fetches ffmpeg's zip, checks it, and unpacks what the app keeps into a folder of <paramref name="work"/>.</summary>
    public static async Task<string> FetchFfmpeg(HelperPin pin, string work, IReport report, CancellationToken ct)
    {
        var zip = Path.Combine(work, "ffmpeg.zip");
        await Fetch(new Asset(Path.GetFileName(new Uri(pin.Url).AbsolutePath), pin.Url, pin.Size, null), zip, pin.Sha256, report, ct);
        var into = Path.Combine(work, "ffmpeg");
        if (Directory.Exists(into)) Directory.Delete(into, true);
        Directory.CreateDirectory(into);
        using (var archive = ZipFile.OpenRead(zip))
        {
            foreach (var entry in archive.Entries)
            {
                // the same files the app keeps (src\helpers.rs Helper::place): the two programs from bin, and the license
                var parts = entry.FullName.Split('/', '\\');
                string? name = parts switch
                {
                    [_, "bin", var exe] when exe.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase) || exe.Equals("ffprobe.exe", StringComparison.OrdinalIgnoreCase) => exe.ToLowerInvariant(),
                    [_, "LICENSE" or "README.txt"] => parts[1],
                    _ => null,
                };
                if (name != null) entry.ExtractToFile(Path.Combine(into, name), true);
            }
        }
        File.Delete(zip);
        if (!File.Exists(Path.Combine(into, "ffmpeg.exe")) || !File.Exists(Path.Combine(into, "ffprobe.exe")))
            throw new Problem("ffmpeg's download didn't hold its programs.", retry: false);
        Log.Write($"ffmpeg {pin.Version}: checked and unpacked");
        return into;
    }

    /// <summary>
    /// Puts the unpacked ffmpeg at &lt;data folder&gt;\tools\ffmpeg in one step, the way the app does: built beside it first,
    /// then swapped in, so the app never meets half of it.
    /// </summary>
    public static void PlaceFfmpeg(string unpacked, string dataFolder)
    {
        var tools = Path.Combine(dataFolder, "tools");
        var home = Path.Combine(tools, "ffmpeg");
        Directory.CreateDirectory(tools);
        var fresh = Path.Combine(tools, $".ffmpeg-new-{Environment.ProcessId}");
        var old = Path.Combine(tools, $".ffmpeg-old-{Environment.ProcessId}");
        foreach (var d in new[] { fresh, old }) if (Directory.Exists(d)) Directory.Delete(d, true);
        Directory.CreateDirectory(fresh);
        foreach (var f in Directory.GetFiles(unpacked)) File.Copy(f, Path.Combine(fresh, Path.GetFileName(f)));
        if (Directory.Exists(home)) Directory.Move(home, old);
        try { Directory.Move(fresh, home); }
        catch
        {
            if (Directory.Exists(old) && !Directory.Exists(home)) Directory.Move(old, home);
            throw;
        }
        try { if (Directory.Exists(old)) Directory.Delete(old, true); } catch { }
        Log.Write($"ffmpeg placed at {home}");
    }

    static async Task<bool> Matches(string exe, Manifest m, CancellationToken ct) =>
        File.Exists(exe) && new FileInfo(exe).Length == m.Size && await Verify.Sha256(exe, ct) == m.Sha256;

    static bool Writable(string folder)
    {
        try
        {
            var probe = Path.Combine(folder, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Puts the exe in <paramref name="folder"/> in one step (a whole new file or the old one, never half of one).</summary>
    public static string Place(string exe, string folder)
    {
        Directory.CreateDirectory(folder);
        var final = Path.Combine(folder, ExeName);
        var part = final + ".part";
        try
        {
            File.Copy(exe, part, true);
            File.Move(part, final, true);
            Log.Write($"Music Tool placed at {final}");
            return final;
        }
        catch (IOException e) when (e is not FileNotFoundException and not DirectoryNotFoundException)
        {
            try { File.Delete(part); } catch { }
            Log.Write($"Music Tool could not be placed: {e.Message}");
            throw new NeedsClosing("Close the Music Tool first, then click Install.");
        }
        catch (UnauthorizedAccessException e)
        {
            try { File.Delete(part); } catch { }
            Log.Write($"Music Tool could not be placed: {e.Message}");
            // a folder that takes new files but not this one: the exe is running (Windows says "access denied" then)
            if (File.Exists(final) && Writable(folder)) throw new NeedsClosing("Close the Music Tool first, then click Install.");
            throw new Problem($"Couldn't write to {folder}.");
        }
    }

    /// <summary>A Start menu shortcut, and a desktop one unless the desktop already has one to this exe.</summary>
    public static void Shortcuts(string exe, string startMenuLink, string desk)
    {
        const string about = "Bradley's Music Tool";
        try
        {
            if (!File.Exists(startMenuLink) || !Same(ShellLink.Target(startMenuLink), exe)) ShellLink.Write(startMenuLink, exe, about);
        }
        catch (Exception e) { Log.Write($"Start menu shortcut: {e.Message}"); }
        try
        {
            if (string.IsNullOrEmpty(desk)) return;
            var onDesk = Directory.EnumerateFiles(desk, "*.lnk").Any(l => Same(ShellLink.Target(l), exe));
            if (!onDesk) ShellLink.Write(Path.Combine(desk, "Bradley's Music Tool.lnk"), exe, about);
        }
        catch (Exception e) { Log.Write($"desktop shortcut: {e.Message}"); }
    }

    static bool Same(string? a, string b) => a != null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
