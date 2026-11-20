# Cadence Installer

One small file to give people. It installs Cadence, Bradley's music player, with one click.

**[Download Cadence Installer](https://github.com/BradChadley/cadence-installer/releases/latest/download/Cadence-Installer.exe)** (Windows 10 and 11, 64-bit, about 22 MB)

It used to be called Get Bradley's apps. Old links still work: each release attaches the same file under both names, `Cadence-Installer.exe` and the old `Get-Bradleys-apps.exe`, and its `SHA256SUMS.txt` lists both. GitHub follows the repository's old name to the new one, but not a file's old name, so the old file name stays on every release. The link to give people is the one above.

From 1.1.1 the file is signed (publisher: Nick Marrone, through Microsoft's Artifact Signing). Windows may still say "Windows protected your PC" for the first downloads while it learns to trust a new signer: click "More info", then "Run anyway".

**Bradley's Music Tool is part of Cadence now.** Its recommendations, album downloads and library repairs live in Cadence from version 0.30.0, so this installs only Cadence. Versions 1.0.x of this file also offered the Music Tool. For someone who has it, this offers to remove the old copy (a box, ticked to start with) once Cadence is installed and checked.

- **It never needs a new version of its own.** Each time it runs it asks GitHub for Cadence's newest release, so a new Cadence release is picked up with nothing to do here.
- **Cadence stays its own app.** It is installed by its own installer (run quietly), so it keeps its own updates and its own line in Windows' Apps list.
- **No admin rights.** It installs for the Windows account only.
- **Nothing runs unchecked.** Cadence's installer must match the SHA-256 GitHub records for it and the one in the release's `SHA256SUMS.txt` (they must agree). A download that fails is fetched once more, then refused.

## What it handles

| When | It does |
|---|---|
| Cadence is already on the PC | Says so ("On this PC, version 0.30.0, the newest."). Install puts the newest version over it. |
| Cadence is on the PC but damaged (files missing) | Says so in amber ("On this PC, but some of its files are missing. Install repairs it."). Install puts it right, and the end says "Repaired". Settings and the music library live in other folders and are never touched. Only sure signs count, so a working install is never called damaged. |
| The Music Tool is on the PC (its program in `%LOCALAPPDATA%\Programs\Bradley's Music Tool`, or a Start menu shortcut into that folder) | Shows a box under the card, ticked to start with: "Also remove the old Music Tool (it now lives in Cadence)". With no Music Tool there is no box. Unticked, nothing of it is touched. Left ticked, and only after Cadence is installed and passes its checks (never if Cadence's install failed), it sends that folder and its Start menu and desktop shortcuts to the Recycle Bin, and removes the scheduled task "Music Backup (nightly)" only when that task runs the program in that folder. Its settings, reports and undo history (`%LOCALAPPDATA%\music-recs`) stay. The end says what was removed and that it's in the Recycle Bin. |
| Part of the removal fails (a file in use) | Cadence still counts as installed. The rest is removed, and the line names what's left and where, for the person to delete. |
| The Music Tool is open | It never holds Cadence up. Cadence installs, then the line says the Music Tool wasn't removed because it's open, with "Try again". |
| Cadence is open | Says "Close Cadence first, then click Install." before anything starts. |
| GitHub's API is busy (60 questions an hour per address) | Reads the release from github.com's own "latest" link instead; every check still holds. |
| The connection drops | Carries on where it stopped, up to five tries; a connection silent for 30 seconds counts as dropped. |
| Something still fails | A short line on the card with "Try again" and "Download page". "See what happened" opens the log. |
| The window is closed while the installer runs | Waits: an install is never cut off halfway. |
| A release renames its files | The installer is found by kind (the file ending `Setup.exe`), not by exact name. |

It writes one plain log per run: `%TEMP%\Cadence Installer.log`.

**What would need a new build here:** only a change in how Cadence is published. For example, Cadence leaving Velopack or moving to another GitHub repository.

## Making a new build

Needs the .NET 10 SDK and Visual Studio's "Desktop development with C++". The native build uses its linker.

```
pwsh -File build.ps1 -Check
```

That writes `out\Cadence Installer.exe`, about 22 MB, one file that needs nothing installed, and `out\release` with the files a release attaches: `Cadence-Installer.exe`, the same file as `Get-Bradleys-apps.exe` for old links, and `SHA256SUMS.txt` listing both. `-Check` then downloads and checks Cadence into `work\check` and installs nothing. If `dotnet` is not on PATH, pass `-Dotnet <path to dotnet.exe>`.

The build refuses to finish if the exe names this PC's user folder or project folders. Other words it must never contain go in `work\private-words.txt`, one per line. That file stays out of git.

To change Cadence's picture, copy its new `.ico` over `src\Assets\cadence.ico` and run `pwsh -File tools\make-icons.ps1`. The installer's own icon is drawn by the same script.

## Trying it without installing anything

| Command | What it does |
|---|---|
| `"Cadence Installer.exe" --check <folder>` | No window. Downloads and checks Cadence inside `<folder>`, and lists what it would remove of the old Music Tool on this PC (it only reads). Exit code 0 means all good. |
| `"Cadence Installer.exe" --health <folder>` | No window. The damage check: this PC's Cadence must come out whole (it only reads it), and made-up damaged copies in `<folder>` must be caught. Then the Music Tool removal, on a made-up PC in `<folder>`. |
| `... --check <folder> --no-api` | The same, the way used when GitHub's API is busy. |
| `... --check <folder> --tamper` | Spoils the download. The check must then refuse it. |
| `"Cadence Installer.exe" --test-run <folder>` | The window as usual, but it only downloads and checks Cadence, and removes the old Music Tool from a made-up PC it builds in `<folder>\pretend-pc` (folder, shortcuts, a task list and a Recycle Bin, all files there). The real PC's Music Tool, Task Scheduler and Recycle Bin can't be reached in a test run. Add `--auto` to press Install and close by itself, `--tool-running` to have the made-up Music Tool open, `--no-tool` to leave it out. |
| `"Cadence Installer.exe" --shot <state> <png>` | Draws the window in a made-up state (fresh, here, tool, tool-off, tool-closing, tool-left, installing, checking, done, error, damaged, repaired, open) into a picture. |

## How it is built

C# and Avalonia, the same as Cadence, compiled ahead of time to one native exe that starts in about a fifth of a second. Skia and HarfBuzz (drawing and text) ride inside, compressed. They are unpacked once to `%TEMP%\CadenceInstaller-native`. The window draws in software rather than through the graphics card, so no graphics driver can break it.

| File | What it holds |
|---|---|
| `src\Apps.cs` | How Cadence is found on the PC, fetched, checked and installed |
| `src\OldMusicTool.cs` | Finding and removing the old Music Tool, and the made-up PC the tests use |
| `src\ShellLink.cs` | Reading Windows shortcuts (and writing the made-up ones) |
| `src\Releases.cs` | GitHub's API, and the way without it |
| `src\Net.cs` | Downloads with resume and retries, and the log |
| `src\Verify.cs` | Checksums |
| `src\MainWindow.cs` | The window |
| `src\CheckMode.cs` | The no-window tests |
| `src\Shot.cs` | The pictures |
| `src\Natives.cs` | Unpacking Skia and HarfBuzz |

Signing: see [SIGNING.md](SIGNING.md).

## License

GPL 3, the same as Bradley's Music Tool. See [LICENSE](LICENSE).
