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
            var exe = Path.Combine(folder, "Cadence.exe");
            if (!File.Exists(exe)) exe = Path.Combine(folder, "current", "Cadence.exe");
            if (!File.Exists(exe)) continue;
            var version = key.GetValue("DisplayVersion") as string ?? ProductVersion(Path.Combine(folder, "current", "Cadence.exe"));
            return new Installed(version, exe, folder);
        }
        // installed without an entry in Apps (should not happen with Velopack, but costs nothing to look)
        var standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cadence");
        var current = Path.Combine(standard, "current", "Cadence.exe");
        return File.Exists(current) ? new Installed(ProductVersion(current), File.Exists(Path.Combine(standard, "Cadence.exe")) ? Path.Combine(standard, "Cadence.exe") : current, standard) : null;
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

    public override async Task<string> Install(string work, IReport report, bool testRun, CancellationToken ct)
    {
        var r = await Newest(ct);
        // the signed file list first: nothing is fetched for an exe that could not be trusted anyway
        var listAsset = r.Find(n => n.Equals("update.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new Problem("The newest Music Tool release doesn't have its signed file list.", retry: false);
        var listText = await Net.Text(listAsset.Url, ct, most: 64 * 1024) ?? throw new Problem("Couldn't reach GitHub. Check your internet connection.");
        var m = Manifest.ReadSigned(listText) ?? throw new Problem("The Music Tool download couldn't be confirmed as Bradley's, so it wasn't used.", retry: false);
        Log.Write($"Music Tool {m.Version}: signed, {m.Size} bytes, SHA-256 {m.Sha256}");

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
            Look();
            return ProductVersion(final) ?? m.Version;
        }
        finally { Gate.Release(); }
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
