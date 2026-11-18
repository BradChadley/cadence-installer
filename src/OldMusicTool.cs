using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BradleysApps;

/// <summary>
/// Everything removing the old Music Tool may touch: its folders, its scheduled task, whether it runs, and how a file
/// goes away. Made in one of two ways only. <see cref="ThisPc"/> is the real PC and refuses to exist during a test.
/// <see cref="Pretend"/> is a made-up PC inside a test folder, whose task list, Recycle Bin and "running" are files
/// in that folder: nothing in it reaches the real Task Scheduler, Recycle Bin or Music Tool.
/// </summary>
public sealed partial class ToolPlace
{
    public required string ProgramFolder { get; init; }
    public required string StartMenu { get; init; }
    public required string Desktop { get; init; }
    /// <summary>The only folders this place may change anything in (every path is checked against them first).</summary>
    public required IReadOnlyList<string> Allowed { get; init; }
    public required Func<string, bool> Running { get; init; }
    /// <summary>The program a scheduled task runs, or null when there is no such task.</summary>
    public required Func<string, string?> TaskProgram { get; init; }
    public required Func<string, bool> DeleteTask { get; init; }
    /// <summary>Sends a file or folder to the Recycle Bin (never a hard delete); true when it is gone.</summary>
    public required Func<string, bool> Recycle { get; init; }

    /// <summary>Throws unless <paramref name="path"/> is inside one of <see cref="Allowed"/>.</summary>
    public void Guard(string path)
    {
        var full = Path.GetFullPath(path);
        if (!Allowed.Any(a => OldMusicTool.Inside(full, a)))
            throw new InvalidOperationException($"refused: {full} is outside what may be changed");
    }

    /// <summary>True during --test-run (and the --health removal test): the real PC is then out of reach.</summary>
    static bool Testing => Options.TestFolder != null;

    // ---------------------------------------------------------------- the real PC

    public static ToolPlace ThisPc()
    {
        if (Testing) throw new InvalidOperationException("the real PC is out of reach in a test");
        var program = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Bradley's Music Tool");
        var start = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return new ToolPlace
        {
            ProgramFolder = program, StartMenu = start, Desktop = desk,
            Allowed = [program, start, desk],
            Running = RealRunning, TaskProgram = RealTaskProgram, DeleteTask = RealDeleteTask, Recycle = RealRecycle,
        };
    }

    static void NotInTest() { if (Testing) throw new InvalidOperationException("the real PC is out of reach in a test"); }

    static bool RealRunning(string folder)
    {
        var open = false;
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(OldMusicTool.ExeName)))
        {
            string? path;
            try { path = p.MainModule?.FileName; } catch { path = ""; } // a process we may not read counts, to be safe
            finally { p.Dispose(); }
            open |= path != null && (path.Length == 0 || OldMusicTool.Inside(path, folder));
        }
        return open;
    }

    static (int Code, string Out) Schtasks(params string[] args)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        var outText = p.StandardOutput.ReadToEndAsync();
        _ = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(30_000)) { try { p.Kill(); } catch { } return (-1, ""); }
        return (p.ExitCode, outText.Result);
    }

    /// <summary>Reads the task's definition (read only) and returns the program its first action runs.</summary>
    static string? RealTaskProgram(string task)
    {
        NotInTest();
        try
        {
            var (code, xml) = Schtasks("/query", "/tn", task, "/xml");
            if (code != 0) return null;
            var start = xml.IndexOf("<Command>", StringComparison.Ordinal);
            var end = start < 0 ? -1 : xml.IndexOf("</Command>", start, StringComparison.Ordinal);
            if (end < 0) return "";
            return Environment.ExpandEnvironmentVariables(System.Net.WebUtility.HtmlDecode(xml[(start + 9)..end]).Trim().Trim('"'));
        }
        catch (Exception e) { Log.Write($"task {task} could not be read: {e.Message}"); return ""; }
    }

    static bool RealDeleteTask(string task)
    {
        NotInTest();
        try { return Schtasks("/delete", "/tn", task, "/f").Code == 0; }
        catch (Exception e) { Log.Write($"task {task} not removed: {e.Message}"); return false; }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct FileOp
    {
        public IntPtr Hwnd; public uint Func; public IntPtr From; public IntPtr To; public ushort Flags;
        public int Aborted; public IntPtr NameMappings; public IntPtr Title;
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHFileOperationW")]
    private static partial int SHFileOperation(ref FileOp op);

    /// <summary>To the Recycle Bin through Windows' own file operation. If Windows could only delete it for good, it asks first.</summary>
    static bool RealRecycle(string path)
    {
        NotInTest();
        const uint delete = 3;
        const ushort allowUndo = 0x40, noConfirmation = 0x10, silent = 0x4, noErrorUi = 0x400, wantNukeWarning = 0x4000;
        var from = Marshal.StringToHGlobalUni(Path.GetFullPath(path) + "\0"); // the list ends with two nulls
        try
        {
            var op = new FileOp { Func = delete, From = from, Flags = allowUndo | noConfirmation | silent | noErrorUi | wantNukeWarning };
            var code = SHFileOperation(ref op);
            if (code != 0 || op.Aborted != 0) Log.Write($"recycle {path}: code {code}, stopped {op.Aborted != 0}");
            return code == 0 && op.Aborted == 0 && !File.Exists(path) && !Directory.Exists(path);
        }
        finally { Marshal.FreeHGlobal(from); }
    }

    // ---------------------------------------------------------------- a made-up PC, for tests

    /// <summary>A made-up PC inside <paramref name="root"/>: every path, the task list and the Recycle Bin are in there.</summary>
    public static ToolPlace Pretend(string root)
    {
        root = Path.GetFullPath(root);
        string Task(string name) => Path.Combine(root, "Task Scheduler", name + ".txt");
        return new ToolPlace
        {
            ProgramFolder = Path.Combine(root, "AppData", "Local", "Programs", "Bradley's Music Tool"),
            StartMenu = Path.Combine(root, "Start menu"),
            Desktop = Path.Combine(root, "Desktop"),
            Allowed = [root],
            Running = _ => File.Exists(Path.Combine(root, "music-tool-is-running")),
            TaskProgram = name => File.Exists(Task(name)) ? File.ReadAllText(Task(name)).Trim() : null,
            DeleteTask = name => { File.Delete(Task(name)); return true; },
            Recycle = path =>
            {
                var bin = Path.Combine(root, "Recycle Bin", $"{Path.GetFileName(path)}-{Guid.NewGuid():N}");
                Directory.CreateDirectory(Path.GetDirectoryName(bin)!);
                try
                {
                    if (Directory.Exists(path)) Directory.Move(path, bin); else File.Move(path, bin);
                    return true;
                }
                catch (IOException e) { Log.Write($"(pretend) recycle {path}: {e.Message}"); return false; }
                catch (UnauthorizedAccessException e) { Log.Write($"(pretend) recycle {path}: {e.Message}"); return false; }
            },
        };
    }

    /// <summary>
    /// Builds an old Music Tool on the made-up PC, the way this installer's 1.0.x left it: the program folder, a Start
    /// menu and a desktop shortcut, its nightly task running <paramref name="taskProgram"/> (the exe by default), and
    /// beside them things that must stay: its settings folder, another shortcut, another task.
    /// </summary>
    public static ToolPlace MakePretend(string root, string? taskProgram = null)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) Directory.Delete(root, true);
        var p = Pretend(root);
        var exe = Path.Combine(p.ProgramFolder, OldMusicTool.ExeName);
        Directory.CreateDirectory(p.ProgramFolder);
        File.WriteAllText(exe, "a made-up Music Tool");
        File.WriteAllText(Path.Combine(p.ProgramFolder, "LICENSE"), "made up");
        ShellLink.Write(Path.Combine(p.StartMenu, "Bradley's Music Tool.lnk"), exe, "made up");
        ShellLink.Write(Path.Combine(p.Desktop, "Bradley's Music Tool.lnk"), exe, "made up");
        var settings = Path.Combine(root, "AppData", "Local", "music-recs");
        Directory.CreateDirectory(settings);
        File.WriteAllText(Path.Combine(settings, "settings.json"), "{}");
        ShellLink.Write(Path.Combine(p.Desktop, "Something else.lnk"), Path.Combine(settings, "settings.json"), "made up");
        Directory.CreateDirectory(Path.Combine(root, "Task Scheduler"));
        File.WriteAllText(Path.Combine(root, "Task Scheduler", OldMusicTool.TaskName + ".txt"), taskProgram ?? exe);
        File.WriteAllText(Path.Combine(root, "Task Scheduler", "Something else.txt"), Path.Combine(root, "other.exe"));
        return p;
    }
}

/// <summary>
/// Bradley's Music Tool, which this used to install too. It is part of Cadence now (0.30.0), so when the person leaves
/// the box ticked, after Cadence is installed and checked, the old copy this put on the PC is removed: its program folder and shortcuts go to the
/// Recycle Bin, and its nightly backup task is removed when it runs that program. Its settings (music-recs) stay.
/// </summary>
public static class OldMusicTool
{
    public const string ExeName = "bradleys-music-tool.exe";
    public const string TaskName = "Music Backup (nightly)";
    /// <summary>The checkbox under the card, ticked to start with; shown only when the Music Tool is here.</summary>
    public const string Choice = "Also remove the old Music Tool (it now lives in Cadence)";

    public static bool Inside(string path, string folder)
    {
        try
        {
            var p = Path.GetFullPath(path.Trim().Trim('"')).TrimEnd('\\');
            var f = Path.GetFullPath(folder).TrimEnd('\\');
            return p.Equals(f, StringComparison.OrdinalIgnoreCase) || p.StartsWith(f + "\\", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Its shortcuts: in the Start menu (two folders deep) and on the desktop, any that open something in its folder.</summary>
    static List<string> Links(ToolPlace place)
    {
        var found = new List<string>();
        foreach (var (dir, deep) in new[] { (place.StartMenu, true), (place.Desktop, false) })
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
            try
            {
                foreach (var lnk in Directory.EnumerateFiles(dir, "*.lnk", new EnumerationOptions { RecurseSubdirectories = deep, MaxRecursionDepth = 2, IgnoreInaccessible = true }))
                    if (ShellLink.Target(lnk) is { } target && Inside(target, place.ProgramFolder)) found.Add(lnk);
            }
            catch (Exception e) { Log.Write($"shortcuts in {dir}: {e.Message}"); }
        }
        return found;
    }

    /// <summary>True when its program is in its usual folder, or a Start menu shortcut opens something there.</summary>
    public static bool Here(ToolPlace place)
    {
        try
        {
            var here = File.Exists(Path.Combine(place.ProgramFolder, ExeName)) || Links(place).Any(l => Inside(l, place.StartMenu));
            Log.Write($"Music Tool: {(here ? "on this PC, so removing it is offered" : "not on this PC")}");
            return here;
        }
        catch (Exception e) { Log.Write($"Music Tool: looking for it failed: {e.Message}"); return false; }
    }

    public static bool Running(ToolPlace place) => place.Running(place.ProgramFolder);

    /// <summary>What <see cref="Remove"/> would do, in plain lines. Reads only: nothing is changed.</summary>
    public static List<string> Plan(ToolPlace place)
    {
        var lines = new List<string>();
        var program = place.TaskProgram(TaskName);
        if (program == null) lines.Add($"no task \"{TaskName}\"");
        else if (Inside(program, place.ProgramFolder)) lines.Add($"would remove the task \"{TaskName}\" (it runs {program})");
        else lines.Add($"would leave the task \"{TaskName}\": it runs {(program.Length == 0 ? "something unreadable" : program)}, not the Music Tool in its folder");
        foreach (var l in Links(place)) lines.Add($"would recycle the shortcut {l}");
        if (Directory.Exists(place.ProgramFolder)) lines.Add($"would recycle the folder {place.ProgramFolder}");
        if (Running(place)) lines.Add("it is running now, so after Cadence installs, removing it would wait for it to close (Try again)");
        return lines;
    }

    /// <summary>What a removal did: plain names of what went, and what is left with where it is.</summary>
    public sealed record Result(List<string> Removed, List<string> Left);

    /// <summary>
    /// Removes it. Call only after Cadence is installed and checked. Throws <see cref="NeedsClosing"/> while it runs
    /// (nothing is changed then). Each step stands alone: one that fails is named in <see cref="Result.Left"/>.
    /// </summary>
    public static Result Remove(ToolPlace place)
    {
        if (Running(place)) throw new NeedsClosing("it's open");
        var removed = new List<string>();
        var left = new List<string>();
        // the task first, so it never runs against a missing program
        var program = place.TaskProgram(TaskName);
        if (program != null && Inside(program, place.ProgramFolder))
        {
            if (place.DeleteTask(TaskName)) { removed.Add("its nightly backup task"); Log.Write($"Music Tool: task \"{TaskName}\" removed"); }
            else left.Add($"its nightly backup task in Task Scheduler: {TaskName}");
        }
        else if (program != null) Log.Write($"Music Tool: task \"{TaskName}\" runs {program}, not the Music Tool in its folder: left as it is");
        foreach (var lnk in Links(place))
        {
            var what = Inside(lnk, place.StartMenu) ? "its Start menu shortcut" : "its desktop shortcut";
            if (Gone(place, lnk)) { removed.Add(what); Log.Write($"Music Tool: shortcut {lnk} recycled"); }
            else left.Add($"{what}: {lnk}");
        }
        if (Directory.Exists(place.ProgramFolder))
        {
            if (Gone(place, place.ProgramFolder)) { removed.Add("its folder"); Log.Write($"Music Tool: folder {place.ProgramFolder} recycled"); }
            else left.Add($"its folder: {place.ProgramFolder}");
        }
        return new Result(removed, left);
    }

    /// <summary>The line the window shows once Cadence is done; Amber means something was skipped or left; Retry offers "Try again".</summary>
    public sealed record Outcome(string? Line, bool Amber, bool Retry);

    /// <summary>
    /// The one place that decides, after Cadence's install: nothing at all unless the Music Tool is here, the person left
    /// the box ticked, and Cadence installed and passed its checks. A removal that fails only changes this line: Cadence
    /// stays installed.
    /// </summary>
    public static Outcome AfterInstall(ToolPlace place, bool here, bool chosen, bool cadenceInstalled)
    {
        if (!here || !chosen || !cadenceInstalled)
        {
            Log.Write($"Music Tool: not removed ({(!here ? "not on this PC" : !chosen ? "the box was unticked" : "Cadence didn't install")})");
            return new Outcome(null, false, false);
        }
        try
        {
            var r = Remove(place);
            var recycled = r.Removed.Where(x => x != "its nightly backup task").ToList();
            var bin = recycled.Count > 0 ? $" {Capital(List(recycled))} {(recycled.Count == 1 ? "is" : "are")} in the Recycle Bin." : "";
            var task = r.Removed.Contains("its nightly backup task") ? " Its nightly backup task is gone." : "";
            if (r.Left.Count == 0) return new Outcome($"Removed the old Music Tool.{bin}{task}", false, false);
            return new Outcome($"Removed only part of the old Music Tool (a file may be in use).{bin}{task} Still here, for you to delete:\n{string.Join("\n", r.Left)}", true, false);
        }
        catch (NeedsClosing)
        {
            return new Outcome("The old Music Tool is open, so it wasn't removed. Close it, then click Try again.", true, true);
        }
        catch (Exception e)
        {
            Log.Write($"Music Tool: not removed: {e}");
            return new Outcome($"The old Music Tool wasn't removed ({e.Message}). It's still in {place.ProgramFolder}.", true, false);
        }
    }

    static string List(List<string> items) => items.Count switch { 1 => items[0], 2 => $"{items[0]} and {items[1]}", _ => string.Join(", ", items[..^1]) + ", and " + items[^1] };
    static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    static bool Gone(ToolPlace place, string path)
    {
        try { place.Guard(path); return place.Recycle(path); }
        catch (Exception e) { Log.Write($"Music Tool: {path} not removed: {e.Message}"); return false; }
    }
}