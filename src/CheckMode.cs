namespace BradleysApps;

/// <summary>
/// Downloads and checks both apps with no window and installs nothing: the test against the real public releases.
/// Prints one line per step; exit code 0 when both downloads passed every check (or, with --tamper, when every
/// spoiled download was refused).
/// </summary>
public static class CheckMode
{
    sealed class Printer(string name, bool tamper, string folder) : IReport
    {
        long last = -1;
        public void Downloading(long have, long? total)
        {
            if (total is { } t && have == t && last != have) { last = have; Console.WriteLine($"  {name}: downloaded {have:N0} bytes"); }
        }
        public void Step(string words) { last = -1; Console.WriteLine($"  {name}: {words}"); if (words == "Checking") Spoil(); }

        // with --tamper: flips one byte of whatever just arrived, so the check that follows must refuse it
        void Spoil()
        {
            if (!tamper) return;
            var newest = new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            if (newest == null || newest.Length == 0) return;
            using var s = newest.Open(FileMode.Open, FileAccess.ReadWrite);
            s.Position = newest.Length / 2;
            var b = s.ReadByte(); s.Position = newest.Length / 2; s.WriteByte((byte)(b ^ 0xFF));
            Console.WriteLine($"  {name}: spoiled one byte of {newest.Name}");
        }
    }

    public static async Task<int> Run(string folder, bool noApi, bool tamper)
    {
        Directory.CreateDirectory(folder);
        Console.WriteLine($"Get Bradley's apps, check only (nothing is installed), into {Path.GetFullPath(folder)}");
        if (noApi) Releases.ForceFallback = true;
        var failures = 0;
        // the signature check itself: the live signed file list passes; any change to it does not
        if (!noApi)
        {
            var json = await Net.Text("https://github.com/BradChadley/bradleys-music-tool/releases/latest/download/update.json", CancellationToken.None);
            var real = json == null ? null : Manifest.ReadSigned(json);
            Console.WriteLine($"Signature test: the live update.json {(real != null ? "passes" : "DOES NOT PASS")}");
            if (real == null) failures++;
            else
            {
                var changes = new (string What, string Text)[]
                {
                    ("another checksum", json!.Replace(real.Sha256, real.Sha256[..63] + (real.Sha256[63] == '0' ? '1' : '0'))),
                    ("another size", json.Replace(real.Size.ToString(), (real.Size + 1).ToString())),
                    ("another version", json.Replace($"\"{real.Version}\"", "\"9.9.9\"")),
                    ("another key", json),
                };
                foreach (var (what, text) in changes)
                {
                    var key = what == "another key" ? "04" + Manifest.PublicKey[2..] : Manifest.PublicKey;
                    var passed = Manifest.ReadSigned(text, key) != null;
                    Console.WriteLine($"  with {what}: {(passed ? "PASSES (wrong)" : "refused")}");
                    if (passed) failures++;
                }
            }
        }
        foreach (var app in new App[] { new Cadence(), new MusicTool() })
        {
            app.Look();
            Console.WriteLine($"{app.Name}: {(app.Here is { } h ? $"on this PC, version {h.Version}, {h.Exe}" : "not on this PC")}, running: {app.IsRunning()}");
            var work = Path.Combine(Path.GetFullPath(folder), app.GetType().Name);
            if (Directory.Exists(work)) Directory.Delete(work, true);
            Directory.CreateDirectory(work);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var r = await app.Newest(CancellationToken.None);
                Console.WriteLine($"  newest release {r.Version} ({(r.FromApi ? "GitHub's API" : "github.com, no API")}), {r.Assets.Count} files");
                var version = await app.Install(work, new Printer(app.Name, tamper, work), testRun: true, CancellationToken.None);
                Console.WriteLine($"  PASSED every check: version {version}, {clock.Elapsed.TotalSeconds:N1} s");
                if (tamper) failures++;
                else if (app is MusicTool) failures += PlaceTest(Path.Combine(work, "music-tool", "bradleys-music-tool.exe"), Path.Combine(Path.GetFullPath(folder), "place-test"));
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
        }
        Console.WriteLine(failures == 0 ? "All good." : $"{failures} not as expected.");
        Console.WriteLine($"Log: {Log.Path}");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Puts the checked Music Tool exe in a stand-in program folder and writes real shortcuts in stand-in Start menu and
    /// desktop folders (all under <paramref name="root"/>), twice, then once more while a program runs from that exe.
    /// Nothing outside <paramref name="root"/> is touched. Returns the number of things not as expected.
    /// </summary>
    static int PlaceTest(string exe, string root)
    {
        var bad = 0;
        void Expect(bool ok, string what) { Console.WriteLine($"  {(ok ? "ok" : "NOT OK")}: {what}"); if (!ok) bad++; }
        if (Directory.Exists(root)) Directory.Delete(root, true);
        var program = Path.Combine(root, "Programs", "Bradley's Music Tool");
        var start = Path.Combine(root, "Start menu", "Bradley's Music Tool.lnk");
        var desk = Path.Combine(root, "Desktop");
        Directory.CreateDirectory(desk);
        Console.WriteLine($"  placing into {root}");
        var final = MusicTool.Place(exe, program);
        MusicTool.Shortcuts(final, start, desk);
        var want = File.ReadAllBytes(exe);
        Expect(File.ReadAllBytes(final).AsSpan().SequenceEqual(want), "the exe in place is byte for byte the checked one");
        Expect(!File.Exists(final + ".part"), "no .part file left");
        Expect(string.Equals(ShellLink.Target(start), final, StringComparison.OrdinalIgnoreCase), "the Start menu shortcut opens it");
        var links = Directory.GetFiles(desk, "*.lnk");
        Expect(links.Length == 1 && string.Equals(ShellLink.Target(links[0]), final, StringComparison.OrdinalIgnoreCase), "one desktop shortcut, opening it");
        // again, as an update over itself: nothing doubled
        MusicTool.Place(exe, program);
        MusicTool.Shortcuts(final, start, desk);
        Expect(Directory.GetFiles(desk, "*.lnk").Length == 1, "a second install adds no second desktop shortcut");
        // a running program in the exe's place (ping, waiting): the install must ask to close it, not fail oddly
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), final, true);
        using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(final, "-n 30 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!)
        {
            Thread.Sleep(500);
            string? said = null;
            try { MusicTool.Place(exe, program); } catch (NeedsClosing e) { said = e.Message; } catch (Exception e) { said = "wrong: " + e.Message; }
            Expect(said == "Close the Music Tool first, then click Install.", $"while it runs: \"{said ?? "it was replaced"}\"");
            p.Kill();
            p.WaitForExit();
        }
        Thread.Sleep(300);
        MusicTool.Place(exe, program);
        Expect(File.ReadAllBytes(final).AsSpan().SequenceEqual(want), "once closed, the install goes through");
        return bad;
    }
}
