namespace BradleysApps;

/// <summary>
/// Downloads and checks Cadence with no window and installs nothing: the test against the real public release.
/// Prints one line per step; exit code 0 when the download passed every check (or, with --tamper, when the spoiled
/// download was refused).
/// </summary>
public static class CheckMode
{
    sealed class Printer(bool tamper, string folder) : IReport
    {
        long last = -1;
        public void Downloading(long have, long? total)
        {
            if (total is { } t && have == t && last != have) { last = have; Console.WriteLine($"  Cadence: downloaded {have:N0} bytes"); }
        }
        public void Step(string words) { last = -1; Console.WriteLine($"  Cadence: {words}"); if (words == "Checking") Spoil(); }

        // with --tamper: flips one byte of whatever just arrived, so the check that follows must refuse it
        void Spoil()
        {
            if (!tamper) return;
            var newest = new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            if (newest == null || newest.Length == 0) return;
            using var s = newest.Open(FileMode.Open, FileAccess.ReadWrite);
            s.Position = newest.Length / 2;
            var b = s.ReadByte(); s.Position = newest.Length / 2; s.WriteByte((byte)(b ^ 0xFF));
            Console.WriteLine($"  Cadence: spoiled one byte of {newest.Name}");
        }
    }

    public static async Task<int> Run(string folder, bool noApi, bool tamper)
    {
        Directory.CreateDirectory(folder);
        Console.WriteLine($"Get Bradley's apps, check only (nothing is installed), into {Path.GetFullPath(folder)}");
        if (noApi) Releases.ForceFallback = true;
        var failures = 0;
        var app = new Cadence();
        app.Look();
        Console.WriteLine($"Cadence: {(app.Here is { } h ? $"on this PC, version {h.Version}, {h.Exe}" : "not on this PC")}, running: {app.IsRunning()}");
        // read only: what an install would do to the old Music Tool. Remove is never called here.
        var place = ToolPlace.ThisPc();
        if (OldMusicTool.Here(place))
        {
            Console.WriteLine($"Bradley's Music Tool: on this PC. The window shows a ticked box, \"{OldMusicTool.Choice}\". Left ticked, after Cadence installs, it:");
            foreach (var line in OldMusicTool.Plan(place)) Console.WriteLine($"  {line}");
            Console.WriteLine("  (nothing was removed: --check only reports)");
        }
        else Console.WriteLine("Bradley's Music Tool: not on this PC, so the window says nothing about it");
        var work = Path.Combine(Path.GetFullPath(folder), nameof(Cadence));
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var r = await app.Newest(CancellationToken.None);
            Console.WriteLine($"  newest release {r.Version} ({(r.FromApi ? "GitHub's API" : "github.com, no API")}), {r.Assets.Count} files");
            var version = await app.Install(work, new Printer(tamper, work), testRun: true, CancellationToken.None);
            Console.WriteLine($"  PASSED every check: version {version}, {clock.Elapsed.TotalSeconds:N1} s");
            if (tamper) failures++;
        }
        catch (Problem p)
        {
            Console.WriteLine($"  REFUSED: {p.Message}{(p.InnerException != null ? $" ({p.InnerException.Message})" : "")}");
            if (!tamper) failures++;
        }
        catch (Exception e)
        {
            Console.WriteLine($"  FAILED unexpectedly: {e}");
            failures++;
        }
        Console.WriteLine(failures == 0 ? "All good." : $"{failures} not as expected.");
        Console.WriteLine($"Log: {Log.Path}");
        return failures == 0 ? 0 : 1;
    }
}

/// <summary>
/// --health &lt;folder&gt;: the damage check, against this PC's real Cadence (it must come out whole: no false alarms)
/// and against made-up damaged copies built in &lt;folder&gt;; then removing the old Music Tool from a made-up PC in
/// &lt;folder&gt;. Reads the real install, never changes it, and never reaches the real Music Tool.
/// </summary>
public static class HealthTest
{
    public static int Run(string folder)
    {
        var bad = 0;
        void Expect(bool ok, string what) { Console.WriteLine($"  {(ok ? "ok" : "NOT OK")}: {what}"); if (!ok) bad++; }
        folder = Path.GetFullPath(folder);
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        Directory.CreateDirectory(folder);

        Console.WriteLine("Cadence");
        var cadence = new Cadence(); cadence.Look();
        if (cadence.Here is { } c)
        {
            Expect(Cadence.Health(c.Folder) == null, $"the real install at {c.Folder} is whole");
            // a stand-in: every file name of the real install, empty, plus its real sq.version and .deps.json
            var fake = Path.Combine(folder, "Cadence");
            foreach (var f in Directory.EnumerateFiles(c.Folder, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(c.Folder, f);
                if (rel.StartsWith("packages", StringComparison.OrdinalIgnoreCase)) continue;
                var to = Path.Combine(fake, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                if (rel.EndsWith("sq.version", StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)) File.Copy(f, to);
                else File.WriteAllBytes(to, []);
            }
            Expect(Cadence.Health(fake) == null, "a stand-in with every file is whole");
            var lib = Path.Combine(fake, "current", "Avalonia.Skia.dll");
            File.Delete(lib);
            Expect(Cadence.Health(fake) == "some of its files are missing", "one library gone: missing files");
            File.WriteAllBytes(lib, []);
            File.Move(Path.Combine(fake, "Update.exe"), Path.Combine(fake, "Update.exe.x"));
            Expect(Cadence.Health(fake) != null, "Update.exe gone: missing files");
            File.Move(Path.Combine(fake, "Update.exe.x"), Path.Combine(fake, "Update.exe"));
            File.Move(Path.Combine(fake, "Cadence.exe"), Path.Combine(fake, "Cadence.exe.x"));
            Expect(Cadence.Health(fake) != null, "the launcher the shortcuts open gone: missing files");
            File.Move(Path.Combine(fake, "Cadence.exe.x"), Path.Combine(fake, "Cadence.exe"));
            File.Delete(Path.Combine(fake, "current", "Cadence.deps.json"));
            Expect(Cadence.Health(fake) == null, "no library list (another way of building): not called damage");
            File.Delete(Path.Combine(fake, "current", "sq.version"));
            Expect(Cadence.Health(fake) != null, "Velopack's sq.version gone: missing files");
        }
        else Console.WriteLine("  (not on this PC: nothing to check, the made-up copies are built from a real one)");

        bad += RemovalTest(Path.Combine(folder, "pretend-pc"));
        Console.WriteLine(bad == 0 ? "All good." : $"{bad} not as expected.");
        return bad == 0 ? 0 : 1;
    }

    /// <summary>
    /// Removing the old Music Tool, on a made-up PC inside <paramref name="root"/> only. It runs as a test run, so the
    /// real PC, Task Scheduler and Recycle Bin refuse to be reached at all (that is checked first).
    /// </summary>
    static int RemovalTest(string root)
    {
        Options.TestFolder = Path.GetDirectoryName(root);
        Console.WriteLine("Removing the old Music Tool (a made-up PC)");
        var failed = 0;
        void Expect(bool ok, string what) { Console.WriteLine($"  {(ok ? "ok" : "NOT OK")}: {what}"); if (!ok) failed++; }
        bool Refused(Action a) { try { a(); return false; } catch (InvalidOperationException) { return true; } }

        Expect(Refused(() => ToolPlace.ThisPc()), "in a test, the real PC can't be reached");
        var p = ToolPlace.MakePretend(root);
        Expect(Refused(() => p.Guard(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))), "a path outside the made-up PC is refused");
        var settings = Path.Combine(root, "AppData", "Local", "music-recs", "settings.json");
        var tasks = Path.Combine(root, "Task Scheduler");
        var task = Path.Combine(tasks, OldMusicTool.TaskName + ".txt");
        string Snapshot() => string.Join("|", Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(f => f[root.Length..]).Order());
        Expect(OldMusicTool.Here(p), "found: the box shows");
        Expect(OldMusicTool.Plan(p).Count == 4, $"the plan names the task, two shortcuts and the folder ({OldMusicTool.Plan(p).Count} lines)");

        // unticked: nothing at all is touched
        var before = Snapshot();
        var o = OldMusicTool.AfterInstall(p, here: true, chosen: false, cadenceInstalled: true);
        Expect(o.Line == null && Snapshot() == before, "found and unticked: no line, nothing touched");

        // Cadence didn't install: nothing is touched either
        o = OldMusicTool.AfterInstall(p, here: true, chosen: true, cadenceInstalled: false);
        Expect(o.Line == null && Snapshot() == before, "Cadence didn't install: nothing touched");

        // open: skipped with Try again, nothing touched (Cadence's install is never held up by it)
        File.WriteAllText(Path.Combine(root, "music-tool-is-running"), "");
        before = Snapshot();
        o = OldMusicTool.AfterInstall(p, true, true, true);
        Expect(o.Retry && o.Amber && Snapshot() == before, $"open: \"{o.Line}\", Try again, nothing touched");
        File.Delete(Path.Combine(root, "music-tool-is-running"));

        // ticked: all of it goes, and the line says where
        o = OldMusicTool.AfterInstall(p, true, true, true);
        Expect(!o.Amber && o.Line!.StartsWith("Removed the old Music Tool.") && o.Line.Contains("Recycle Bin"), $"found and ticked: \"{o.Line}\"");
        Expect(!Directory.Exists(p.ProgramFolder) && Directory.EnumerateDirectories(Path.Combine(root, "Recycle Bin")).Any(), "the folder went to the Recycle Bin");
        Expect(!File.Exists(Path.Combine(p.StartMenu, "Bradley's Music Tool.lnk")) && !File.Exists(Path.Combine(p.Desktop, "Bradley's Music Tool.lnk")), "its Start menu and desktop shortcuts went too");
        Expect(File.Exists(Path.Combine(p.Desktop, "Something else.lnk")), "another shortcut stays");
        Expect(!File.Exists(task) && File.Exists(Path.Combine(tasks, "Something else.txt")), "its task is removed, another task stays");
        Expect(File.Exists(settings), "its settings (music-recs) stay");

        // not found: no box, and nothing to do
        Expect(!OldMusicTool.Here(p), "not found any more: no box");
        Expect(OldMusicTool.AfterInstall(p, here: false, chosen: true, cadenceInstalled: true).Line == null, "not found: no line");

        // a task of the same name that runs something else (Bradley's own backup script) is left alone
        p = ToolPlace.MakePretend(root, @"C:\Program Files\PowerShell\7\pwsh.exe");
        o = OldMusicTool.AfterInstall(p, true, true, true);
        Expect(!o.Amber && File.Exists(task) && !Directory.Exists(p.ProgramFolder), "a task that runs something else stays; the rest goes");

        // a step that fails (a file in use): Cadence stays installed, the rest goes, the reason and the place are shown
        p = ToolPlace.MakePretend(root);
        using (File.Open(Path.Combine(p.ProgramFolder, "LICENSE"), FileMode.Open, FileAccess.Read, FileShare.None))
            o = OldMusicTool.AfterInstall(p, true, true, true);
        Expect(o.Amber && !o.Retry && o.Line!.Contains("in use") && o.Line.Contains(p.ProgramFolder) && !File.Exists(Path.Combine(p.Desktop, "Bradley's Music Tool.lnk")), $"a step failing: \"{o.Line?.Replace("\n", " / ")}\"");
        Options.TestFolder = null;
        return failed;
    }
}
