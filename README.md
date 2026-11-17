# Get Bradley's apps

One small file to give people. They pick Cadence, Bradley's Music Tool, or both, and click Install.

- **It never needs a new version of its own.** Each time it runs it asks GitHub for the newest release of each app, so a new Cadence or Music Tool release is picked up with nothing to do here.
- **Each app stays its own app.** Cadence is installed by its own installer (run quietly), so it keeps its own updates and its own line in Windows' Apps list. The Music Tool has no installer: this puts its exe where the Music Tool's own ship script does, `%LOCALAPPDATA%\Programs\Bradley's Music Tool`, with a Start menu and a desktop shortcut, and from then on it updates itself as usual.
- **No admin rights.** Both install for the Windows account only.
- **Nothing runs unchecked.** Cadence's installer must match the SHA-256 GitHub records for it and the one in the release's `SHA256SUMS.txt` (they must agree). The Music Tool's exe must match the release's `update.json`, which must carry Bradley's signature: the same check, with the same key, the Music Tool's own updater makes. A download that fails is fetched once more, then refused.

## What it handles

| When | It does |
|---|---|
| An app is already on the PC | Says so ("On this PC, version 0.26.8, the newest.") and leaves it unpicked. Picking it installs the newest version over it. |
| The Music Tool was unpacked somewhere else (say, Documents) | Finds it through its shortcuts or the running program, and updates that copy instead of making a second one. |
| An app is open | Says "Close Cadence first, then click Install." before anything starts. |
| GitHub's API is busy (60 questions an hour per address) | Reads the releases from github.com's own "latest" links instead; every check still holds. |
| The connection drops | Carries on where it stopped, up to five tries; a connection silent for 30 seconds counts as dropped. |
| Something still fails | A short line on that card with "Try again" and "Download page". The other app carries on. "See what happened" opens the log. |
| The window is closed while an installer runs | Waits: an install is never cut off halfway. |
| A release renames its files | Files are found by kind (the one ending `Setup.exe`, the `.zip`, `update.json`), not by exact name. |

It writes one plain log per run: `%TEMP%\Get Bradley's apps.log`.

**What would need a new build here:** only a change in how an app is published. For example, the Music Tool getting an installer of its own, Cadence leaving Velopack, either moving to another GitHub repository, or the Music Tool's signing key changing. A new key would break the Music Tool's own updates too, so it would not happen quietly.

## Making a new build

Needs the .NET 10 SDK and Visual Studio's "Desktop development with C++". The native build uses its linker.

```
pwsh -File build.ps1 -Check
```

That writes `out\Get Bradley's apps.exe`, about 22 MB, one file that needs nothing installed. `-Check` then downloads and checks both apps into `work\check` and installs nothing. If `dotnet` is not on PATH, pass `-Dotnet <path to dotnet.exe>`.

The build refuses to finish if the exe names this PC's user folder or project folders. Other words it must never contain go in `work\private-words.txt`, one per line. That file stays out of git.

To change an app's picture, copy its new `.ico` over `src\Assets\cadence.ico` or `src\Assets\bradleys-music-tool.ico` and run `pwsh -File tools\make-icons.ps1`. The chooser's own icon is drawn by the same script.

## Trying it without installing anything

| Command | What it does |
|---|---|
| `"Get Bradley's apps.exe" --check <folder>` | No window. Checks the signature test, downloads and checks both apps, and places the Music Tool with real shortcuts, all inside `<folder>`. Exit code 0 means all good. |
| `... --check <folder> --no-api` | The same, the way used when GitHub's API is busy. |
| `... --check <folder> --tamper` | Spoils each download. Every check must then refuse it. |
| `"Get Bradley's apps.exe" --test-run <folder>` | The window as usual, but it only downloads and checks. Add `--auto` to press Install by itself. |
| `"Get Bradley's apps.exe" --shot <state> <png>` | Draws the window in a made-up state (fresh, here, installing, checking, done, error, open) into a picture. |

## How it is built

C# and Avalonia, the same as Cadence, compiled ahead of time to one native exe that starts in about a fifth of a second. Skia and HarfBuzz (drawing and text) ride inside, compressed. They are unpacked once to `%TEMP%\GetBradleysApps-native`. The window draws in software rather than through the graphics card, so no graphics driver can break it.

| File | What it holds |
|---|---|
| `src\Apps.cs` | Each app: how it is found on the PC, fetched, checked and installed |
| `src\Releases.cs` | GitHub's API, and the way without it |
| `src\Net.cs` | Downloads with resume and retries, and the log |
| `src\Verify.cs` | Checksums and the Music Tool's signature |
| `src\ShellLink.cs` | Reading and writing Windows shortcuts |
| `src\MainWindow.cs` | The window |
| `src\CheckMode.cs` | The no-window test |
| `src\Shot.cs` | The pictures |
| `src\Natives.cs` | Unpacking Skia and HarfBuzz |

Signing: see [SIGNING.md](SIGNING.md).

## License

GPL 3, the same as Bradley's Music Tool. See [LICENSE](LICENSE).
