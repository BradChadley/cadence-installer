using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Simple;

namespace BradleysApps;

public static class Program
{
    /// <summary>
    /// --test-run &lt;folder&gt;   the window as usual, but Cadence is only downloaded and checked into the folder, never installed
    /// --health &lt;folder&gt;     no window: the damage check, on this PC's Cadence (read only) and on made-up damaged copies
    /// --test-run &lt;folder&gt; --auto   the same, and Install is pressed by itself
    /// --check &lt;folder&gt;      no window: downloads and checks Cadence into the folder, prints what it found (exit 0 = all good)
    ///   --no-api             (with --check) reads the releases the way used when GitHub's API is busy
    ///   --tamper             (with --check) spoils the download after it arrives: the check must then refuse it
    /// --shot &lt;state&gt; &lt;png&gt;  draws the window in a made-up state into a picture and quits (for checking the look)
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        Log.Start();
        var at = Array.IndexOf(args, "--check");
        if (at >= 0 && at + 1 < args.Length)
            return CheckMode.Run(args[at + 1], args.Contains("--no-api"), args.Contains("--tamper")).GetAwaiter().GetResult();
        var health = Array.IndexOf(args, "--health");
        if (health >= 0 && health + 1 < args.Length) return HealthTest.Run(args[health + 1]);
        var test = Array.IndexOf(args, "--test-run");
        if (test >= 0 && test + 1 < args.Length) Options.TestFolder = Path.GetFullPath(args[test + 1]);
        var shot = Array.IndexOf(args, "--shot");
        if (shot >= 0 && shot + 2 < args.Length) { Options.ShotState = args[shot + 1]; Options.ShotFile = Path.GetFullPath(args[shot + 2]); }
        Natives.Unpack();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<ChooserApp>().UsePlatformDetect()
        // drawn in software: no graphics driver can break it, and this small window needs no GPU
        .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
        .LogToTrace();
}

public static class Options
{
    public static string? TestFolder;
    public static string? ShotState;
    public static string? ShotFile;
}

public sealed class ChooserApp : Avalonia.Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new SimpleTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
