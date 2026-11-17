using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace BradleysApps;

/// <summary>
/// --shot &lt;state&gt; &lt;png&gt;: the window in a made-up state, drawn into a picture, then closed. For checking the look
/// against the sketch without touching the network or the PC. States: fresh, here, installing, done, error, open.
/// </summary>
static class Shot
{
    public static async Task Take(MainWindow w, Card[] cards, string state, string file)
    {
        var (cadence, tool) = (cards[0], cards[1]);
        void None() { }
        switch (state)
        {
            case "fresh":
                cadence.Say("Version 0.26.8, 98 MB"); tool.Say("Version 1.0.4, 9 MB, plus ffmpeg (109 MB)");
                break;
            case "ffmpeg":
                w.SetHeading("Installing", "You can keep using your PC. This takes about a minute.");
                w.SetButton(null, null); w.SetFoot("");
                foreach (var c in cards) c.Lock(true);
                await Wait(100);
                cadence.Done("Installed, version 0.26.8.", ("Open", None));
                tool.Label("Downloading ffmpeg");
                tool.Downloading(47 * 1048576L, 109 * 1048576L);
                break;
            case "ffmpeg-done":
                w.SetHeading("All set", "You'll find both in your Start menu.");
                w.SetButton("Close", None); w.SetFoot("");
                foreach (var c in cards) c.Lock(true);
                cadence.Done("Installed, version 0.26.8.", ("Open", None));
                tool.Done("Installed, version 1.0.4. ffmpeg is ready too.", ("Open", None));
                break;
            case "here":
                cadence.SetPicked(false); tool.SetPicked(false);
                cadence.Say("On this PC, version 0.26.8, the newest."); tool.Say("On this PC in Documents, version 1.0.3. It will update itself to 1.0.4.");
                w.SetButton("Install", null); w.SetFoot("Pick one to install the newest version over it.");
                break;
            case "installing":
                w.SetHeading("Installing", "You can keep using your PC. This takes about a minute.");
                w.SetButton(null, null); w.SetFoot("");
                foreach (var c in cards) c.Lock(true);
                await Wait(100);
                cadence.Downloading(41 * 1048576L, 98 * 1048576L);
                tool.Done("Installed, version 1.0.4", ("Open", None));
                break;
            case "done":
                w.SetHeading("All set", "You'll find both in your Start menu.");
                w.SetButton("Close", None); w.SetFoot("");
                foreach (var c in cards) c.Lock(true);
                cadence.Done("Installed, version 0.26.8", ("Open", None)); tool.Done("Installed, version 1.0.4", ("Open", None));
                break;
            case "error":
                w.SetHeading("Almost there", "One didn't finish. The other is installed.");
                w.SetButton("Close", None, quiet: true); w.SetFoot("", ("See what happened", None));
                foreach (var c in cards) c.Lock(true);
                await Wait(100);
                cadence.Downloading(62 * 1048576L, 98 * 1048576L);
                await Wait(400);
                cadence.Failed("The connection dropped.", false, ("Try again", None), ("Download page", None));
                tool.Done("Installed, version 1.0.4", ("Open", None));
                break;
            case "damaged":
                cadence.SetPicked(true); tool.SetPicked(false);
                cadence.Say("On this PC, but some of its files are missing. Install repairs it.", Ink.Amber);
                tool.Say("On this PC, version 1.0.4, the newest.");
                break;
            case "repaired":
                tool.SetPicked(false);
                w.SetHeading("All set", "You'll find it in your Start menu.");
                w.SetButton("Close", None); w.SetFoot("");
                foreach (var c in cards) c.Lock(true);
                tool.Opacity = 0.45; tool.Say("On this PC, version 1.0.4, the newest.");
                cadence.Done("Repaired, version 0.26.8", ("Open", None));
                break;
            case "open":
                cadence.Say("Close Cadence first, then click Install.", Ink.Amber); tool.Say("Version 1.0.4, 9 MB");
                break;
            case "checking":
                w.SetHeading("Installing", "You can keep using your PC. This takes about a minute.");
                w.SetButton(null, null); w.SetFoot("");
                foreach (var c in cards) c.Lock(true);
                cadence.Step("Installing"); tool.Step("Checking");
                break;
        }
        await Wait(900);
        var scale = w.RenderScaling;
        var size = new PixelSize((int)(w.Bounds.Width * scale), (int)(w.Bounds.Height * scale));
        using (var bmp = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale)))
        {
            bmp.Render(w);
            bmp.Save(file);
        }
        w.Close();
    }

    static Task Wait(int ms)
    {
        var t = new TaskCompletionSource();
        DispatcherTimer.RunOnce(() => t.SetResult(), TimeSpan.FromMilliseconds(ms));
        return t.Task;
    }
}
