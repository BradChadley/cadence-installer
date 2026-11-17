using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace BradleysApps;

/// <summary>Reads and writes Windows shortcuts (.lnk) through Windows' own ShellLink object.</summary>
public static partial class ShellLink
{
    static readonly Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");
    static readonly Guid IidShellLink = new("000214F9-0000-0000-C000-000000000046");
    static readonly StrategyBasedComWrappers Wrappers = new();

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, IntPtr outer, uint context, in Guid iid, out IntPtr result);

    static IShellLinkW Create()
    {
        var hr = CoCreateInstance(ClsidShellLink, IntPtr.Zero, 1 /* in process */, IidShellLink, out var ptr);
        Marshal.ThrowExceptionForHR(hr);
        try { return (IShellLinkW)Wrappers.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.UniqueInstance); }
        finally { Marshal.Release(ptr); }
    }

    /// <summary>The file a shortcut opens, or null when it is not a shortcut to a file.</summary>
    public static string? Target(string lnk)
    {
        try
        {
            var link = Create();
            ((IPersistFile)link).Load(lnk, 0);
            var buf = Marshal.AllocHGlobal(2 * 1024);
            try
            {
                link.GetPath(buf, 1024, IntPtr.Zero, 0x4 /* the raw path, %variables% as written */);
                var path = Marshal.PtrToStringUni(buf);
                return string.IsNullOrWhiteSpace(path) ? null : Environment.ExpandEnvironmentVariables(path);
            }
            finally { Marshal.FreeHGlobal(buf); ((object)link as ComObject)?.FinalRelease(); }
        }
        catch (Exception e) { Log.Write($"shortcut {lnk} could not be read: {e.Message}"); return null; }
    }

    /// <summary>Writes (or rewrites) a shortcut to <paramref name="exe"/>, with the exe's own icon.</summary>
    public static void Write(string lnk, string exe, string description)
    {
        var link = Create();
        try
        {
            link.SetPath(exe);
            link.SetWorkingDirectory(Path.GetDirectoryName(exe)!);
            link.SetDescription(description);
            link.SetIconLocation(exe, 0);
            Directory.CreateDirectory(Path.GetDirectoryName(lnk)!);
            ((IPersistFile)link).Save(lnk, 1);
        }
        finally { ((object)link as ComObject)?.FinalRelease(); }
    }
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal partial interface IShellLinkW
{
    void GetPath(IntPtr file, int size, IntPtr findData, uint flags);
    void GetIDList(out IntPtr list);
    void SetIDList(IntPtr list);
    void GetDescription(IntPtr name, int size);
    void SetDescription(string name);
    void GetWorkingDirectory(IntPtr dir, int size);
    void SetWorkingDirectory(string dir);
    void GetArguments(IntPtr args, int size);
    void SetArguments(string args);
    void GetHotkey(out short key);
    void SetHotkey(short key);
    void GetShowCmd(out int show);
    void SetShowCmd(int show);
    void GetIconLocation(IntPtr path, int size, out int index);
    void SetIconLocation(string path, int index);
    void SetRelativePath(string path, uint reserved);
    void Resolve(IntPtr hwnd, uint flags);
    void SetPath(string file);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("0000010b-0000-0000-C000-000000000046")]
internal partial interface IPersistFile
{
    void GetClassID(out Guid clsid);
    [PreserveSig] int IsDirty();
    void Load(string file, uint mode);
    void Save(string file, int remember);
    void SaveCompleted(string file);
    void GetCurFile(out IntPtr file);
}
