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
        public void Label(string words) => Console.WriteLine($"  {name}: {words}");
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
        MusicTool.ForceFfmpeg = true; // fetched even when this PC has ffmpeg, to test the way there
        var failures = 0;
        failures += HelpersSignatureTest();
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
                else if (app is MusicTool) failures += PlaceTest(Path.Combine(work, "music-tool", "bradleys-music-tool.exe"), Path.Combine(work, "ffmpeg"), Path.Combine(Path.GetFullPath(folder), "place-test"));
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
    /// helpers.json's signature, with a key made for this test only (the real key never leaves the author's PC):
    /// a file signed with it passes; any change to it, or the same file checked against the real key, does not.
    /// </summary>
    static int HelpersSignatureTest()
    {
        var bad = 0;
        void Expect(bool ok, string what) { Console.WriteLine($"  {(ok ? "ok" : "NOT OK")}: {what}"); if (!ok) bad++; }
        Console.WriteLine("helpers.json signature test (a throwaway key):");
        using var ec = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var q = ec.ExportParameters(false).Q;
        var key = Convert.ToHexStringLower(q.X!) + Convert.ToHexStringLower(q.Y!);
        var p = HelperPin.BuiltIn;
        string Json(string version, string url, long size, string sha, string sig) =>
            "{\"ffmpeg\": {\"version\": \"" + version + "\", \"url\": \"" + url + "\", \"size\": " + size + ", \"sha256\": \"" + sha + "\", \"signature\": \"" + sig + "\"}}";
        var sig = Convert.ToHexStringLower(ec.SignData(System.Text.Encoding.UTF8.GetBytes(HelperPin.SignedText(p.Version, p.Url, p.Size, p.Sha256)),
            System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        Expect(HelperPin.ReadSigned(Json(p.Version, p.Url, p.Size, p.Sha256, sig), key) == p, "signed: passes, with every field as signed");
        Expect(HelperPin.ReadSigned(Json(p.Version, "https://example.com/ffmpeg.zip", p.Size, p.Sha256, sig), key) == null, "another address: refused");
        Expect(HelperPin.ReadSigned(Json(p.Version, p.Url, p.Size, new string('a', 64), sig), key) == null, "another checksum: refused");
        Expect(HelperPin.ReadSigned(Json("9.9.9", p.Url, p.Size, p.Sha256, sig), key) == null, "another version: refused");
        Expect(HelperPin.ReadSigned(Json(p.Version, p.Url, p.Size, p.Sha256, sig)) == null, "checked against Bradley's real key: refused");
        Expect(HelperPin.ReadSigned(Json(p.Version, p.Url.Replace("https://", "http://"), p.Size, p.Sha256, sig), key) == null, "a plain http address: refused");
        Expect(HelperPin.ReadSigned("{}", key) == null && HelperPin.ReadSigned("not json", key) == null, "nonsense: refused, no crash");
        return bad;
    }

    /// <summary>
    /// Puts the checked Music Tool exe in a stand-in program folder and writes real shortcuts in stand-in Start menu and
    /// desktop folders (all under <paramref name="root"/>), twice, then once more while a program runs from that exe.
    /// Nothing outside <paramref name="root"/> is touched. Returns the number of things not as expected.
    /// </summary>
    static int PlaceTest(string exe, string ffmpeg, string root)
    {
        var bad = 0;
        void Expect(bool ok, string what) { Console.WriteLine($"  {(ok ? "ok" : "NOT OK")}: {what}"); if (!ok) bad++; }
        // ffmpeg: the files the app keeps, in a stand-in data folder, twice (the second swaps the first out)
        var data = Path.Combine(root, "music-recs");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        for (var i = 0; i < 2; i++) MusicTool.PlaceFfmpeg(ffmpeg, data);
        var home = Path.Combine(data, "tools", "ffmpeg");
        var kept = Directory.Exists(home) ? Directory.GetFiles(home).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase).ToArray() : [];
        Expect(string.Join(", ", kept) == "ffmpeg.exe, ffprobe.exe, LICENSE, README.txt", $"ffmpeg placed as the app keeps it: {string.Join(", ", kept)}");
        Expect(Directory.GetDirectories(Path.Combine(data, "tools")).Length == 1, "nothing left beside it");
        Expect(MusicTool.FindTool("ffprobe", data) is { } found && (found.StartsWith(home, StringComparison.OrdinalIgnoreCase) || !found.Contains("music-recs")), "the app's way of looking finds it");
        var ver = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(home, "ffmpeg.exe"), "-version") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!;
        var first = ver.StandardOutput.ReadLine() ?? ""; ver.WaitForExit();
        Expect(first.StartsWith("ffmpeg version"), $"it runs: \"{first[..Math.Min(40, first.Length)]}\"");
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

/// <summary>
/// --health &lt;folder&gt;: the damage checks, against this PC's real installs (they must come out whole: no false alarms)
/// and against made-up damaged copies built in &lt;folder&gt;. Reads the real installs, never changes them.
/// </summary>
public static class HealthTest
{
    public static async Task<int> Run(string folder)
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
        else Console.WriteLine("  (not on this PC: only the made-up copies could be checked, and they are built from a real one)");

        Console.WriteLine("Bradley's Music Tool");
        var json = await Net.Text("https://github.com/BradChadley/bradleys-music-tool/releases/latest/download/update.json", CancellationToken.None);
        var m = json == null ? null : Manifest.ReadSigned(json);
        if (m == null) { Console.WriteLine("  NOT OK: the signed file list could not be read"); return bad + 1; }
        var tool = new MusicTool(); tool.Look();
        if (tool.Here is { } t)
        {
            Expect(await MusicTool.Health(t.Exe, m, CancellationToken.None) == null, $"the real install at {t.Exe} is whole");
            var copy = Path.Combine(folder, "tool", "bradleys-music-tool.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            async Task<string?> Check() => await MusicTool.Health(copy, m, CancellationToken.None);
            File.Copy(t.Exe, copy);
            var same = new FileInfo(copy).Length == m.Size;
            Expect(await Check() == null, "a copy of it is whole");
            if (same)
            {
                using (var s = File.Open(copy, FileMode.Open)) { s.Position = s.Length / 2; var b = s.ReadByte(); s.Position = s.Length / 2; s.WriteByte((byte)(b ^ 0xFF)); }
                Expect(await Check() == "its program file is damaged", "one byte spoiled in place: damaged");
            }
            File.Copy(t.Exe, copy, true);
            using (var s = File.Open(copy, FileMode.Append)) s.Write(new byte[4096]);
            Expect(await Check() == null, "same version, another size (a build of his own): not called damage");
            using (var s = File.Open(copy, FileMode.Open)) s.SetLength(200_000);
            Expect(await Check() == "its program file is damaged", "cut short: damaged");
            File.Delete(copy);
            Expect(await Check() == "its program file is missing", "gone: missing");
        }
        else Console.WriteLine("  (not on this PC: nothing to check)");
        Console.WriteLine(bad == 0 ? "All good." : $"{bad} not as expected.");
        return bad == 0 ? 0 : 1;
    }
}
