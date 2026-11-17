using System.IO.Compression;
using System.Runtime.InteropServices;

namespace BradleysApps;

/// <summary>
/// Skia and HarfBuzz (the drawing and the text) ride inside the exe, compressed. At start they are unpacked once
/// into %TEMP%\GetBradleysApps-native\&lt;this build&gt; and Windows is told to look there for them.
/// A debug build has them beside the exe and nothing inside: then this does nothing.
/// </summary>
static partial class Natives
{
    [LibraryImport("kernel32.dll", EntryPoint = "SetDllDirectoryW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetDllDirectory(string path);

    public static void Unpack()
    {
        var asm = typeof(Natives).Assembly;
        var names = asm.GetManifestResourceNames().Where(n => n.StartsWith("native/", StringComparison.Ordinal)).Order().ToList();
        if (names.Count == 0) return;
        // one folder per build: the version and the packed sizes name it, so a newer chooser never meets older files
        long key = 17;
        foreach (var n in names) { using var s = asm.GetManifestResourceStream(n)!; key = key * 31 + s.Length; }
        var parent = Path.Combine(Path.GetTempPath(), "GetBradleysApps-native");
        var dir = Path.Combine(parent, $"{asm.GetName().Version}-{key & 0xFFFFFFFF:x8}");
        var done = Path.Combine(dir, "unpacked");
        try
        {
            if (!File.Exists(done))
            {
                Directory.CreateDirectory(dir);
                foreach (var n in names)
                {
                    var to = Path.Combine(dir, n["native/".Length..]);
                    var part = $"{to}.{Environment.ProcessId}.part";
                    using (var from = asm.GetManifestResourceStream(n)!)
                    using (var unz = new DeflateStream(from, CompressionMode.Decompress))
                    using (var file = File.Create(part)) unz.CopyTo(file);
                    try { File.Move(part, to, true); }
                    catch (IOException) when (File.Exists(to)) { File.Delete(part); } // another copy running, already loaded it
                }
                File.WriteAllText(done, "");
            }
            SetDllDirectory(dir);
        }
        catch (Exception e)
        {
            Log.Write($"the drawing libraries could not be unpacked into {dir}: {e.Message}");
            throw;
        }
        // folders of other builds, when nothing holds them
        try
        {
            foreach (var old in Directory.GetDirectories(parent).Where(d => !d.Equals(dir, StringComparison.OrdinalIgnoreCase)))
                try { Directory.Delete(old, true); } catch { }
        }
        catch { }
    }
}
