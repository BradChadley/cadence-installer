using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace BradleysApps;

/// <summary>
/// --shot &lt;state&gt; &lt;png&gt;: the window in a made-up state, drawn into a picture, then closed. For checking the look
/// without touching the network or the PC. States: fresh, here, tool, tool-open, tool-closing, tool-left, installing, checking, done, error, damaged, repaired, open.
/// </summary>
static class Shot
{
    public static async Task Take(MainWindow w, Card card, string state, string file)
    {
        void None() { }
        void Working() { w.SetHeading("Installing", "You can keep using your PC. This takes about a minute."); w.SetButton(null, null); w.SetFoot(""); }
        w.SetButton("Install", None);
        w.SetFoot("No admin rights needed.");
        switch (state)
        {
            case "fresh":
                card.Say("Version 0.30.0, 98 MB");
                break;
            case "here":
                card.Say("On this PC, version 0.29.4. It will update itself to 0.30.0.");
                w.SetFoot("Install puts the newest version over the one here.");
                break;
            case "tool":
                // someone who has the old Music Tool: the one line under the card
                card.Say("Version 0.30.0, 98 MB");
                w.SayNotice(OldMusicTool.Notice);
                break;
            case "tool-open":
                card.Say("Version 0.30.0, 98 MB");
                w.SayNotice("Close Bradley's Music Tool first, then click Install.", Ink.Amber);
                break;
            case "tool-left":
                w.SetHeading("All set", "You'll find it in your Start menu.");
                w.SetButton("Close", None); w.SetFoot("");
                card.Done("Installed, version 0.30.0.", ("Open", None));
                w.SayNotice("Cadence is installed. These are left from the old Music Tool, for you to delete:\n" + @"its nightly backup task in Task Scheduler: Music Backup (nightly)" + "\n" + @"its folder: C:\Users\Someone\AppData\Local\Programs\Bradley's Music Tool", Ink.Amber);
                break;
            case "tool-closing":
                w.SetHeading("All set", "You'll find it in your Start menu.");
                w.SetButton("Close", None); w.SetFoot("");
                card.Done("Installed, version 0.30.0.", ("Open", None));
                w.SayNotice("Close Bradley's Music Tool to finish removing it.", Ink.Amber, "Try again", None);
                break;
            case "installing":
                Working();
                await Wait(100);
                card.Downloading(41 * 1048576L, 98 * 1048576L);
                break;
            case "checking":
                Working();
                card.Step("Checking");
                break;
            case "done":
                w.SetHeading("All set", "You'll find it in your Start menu.");
                w.SetButton("Close", None); w.SetFoot("");
                card.Done("Installed, version 0.30.0.", ("Open", None));
                w.SayNotice(OldMusicTool.Removed, Ink.Green);
                break;
            case "error":
                w.SetHeading("Not installed", "It didn't finish.");
                w.SetButton("Close", None, quiet: true); w.SetFoot("", ("See what happened", None));
                await Wait(100);
                card.Downloading(62 * 1048576L, 98 * 1048576L);
                await Wait(400);
                card.Failed("The connection dropped.", false, ("Try again", None), ("Download page", None));
                break;
            case "damaged":
                card.Say("On this PC, but some of its files are missing. Install repairs it.", Ink.Amber);
                break;
            case "repaired":
                w.SetHeading("All set", "You'll find it in your Start menu.");
                w.SetButton("Close", None); w.SetFoot("");
                card.Done("Repaired, version 0.30.0.", ("Open", None));
                break;
            case "open":
                card.Say("Close Cadence first, then click Install.", Ink.Amber);
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
