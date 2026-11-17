# helpers.json: which ffmpeg the Music Tool uses

Get Bradley's apps sets up ffmpeg along with the Music Tool, so people can check and convert music from the first start. To know which ffmpeg to get, it reads `helpers.json` from the Music Tool's newest release. That way it follows the app's own pin (`FFMPEG` in `src\helpers.rs`) and never needs a new build when the pin changes.

Until a release carries `helpers.json`, the installer uses the pin it was built with: ffmpeg 9.0.2 essentials.

## What each Music Tool release adds

One more asset beside `update.json`, uploaded as it is: `helpers.json`.

```json
{
  "ffmpeg": {
    "version": "9.0.2",
    "url": "https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip",
    "size": 114768076,
    "sha256": "60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba",
    "signature": "<128 hex: r then s>"
  }
}
```

The fields are copied from `FFMPEG` in `src\helpers.rs`. They are signed with **the same key as `update.json`**, using ECDSA P-256 over the SHA-256 of this text: UTF-8, each line ended by a line feed, the checksum in small hex letters.

```
bradleys-music-tool helpers
ffmpeg
<version>
<url>
<size in bytes>
<sha256>
```

That is a few lines in the Music Tool's own scripts:

- **`tools\sign-update.ps1`:** a `-Helpers` mode that reads `FFMPEG`'s four fields from `src\helpers.rs`, signs the text above, and writes `helpers.json` into `-Out`.
- **`tools\release.ps1`:** calls it next to the update signing, and lists `helpers.json` among the files to upload.

## What the installer does with it

- **Reading it:** it checks the signature with the Music Tool's public key. The address must be https. If the file is missing, unsigned, or doesn't read, the installer uses its built-in pin.
- **When it skips ffmpeg:** it looks where the app looks, in the app's order (PATH, then `%LOCALAPPDATA%\music-recs\tools\ffmpeg`, then winget's packages). If both `ffmpeg.exe` and `ffprobe.exe` are found, it fetches nothing.
- **Fetching:** it downloads the zip from that address, checks size and SHA-256, and keeps what the app keeps: `bin\ffmpeg.exe`, `bin\ffprobe.exe`, `LICENSE` and `README.txt`.
- **Putting it in place:** it puts them in `%LOCALAPPDATA%\music-recs\tools\ffmpeg` in one step. It builds `tools\.ffmpeg-new-<pid>` first, moves any old folder to `tools\.ffmpeg-old-<pid>`, swaps the new one in, then removes the old one.
- **When it fails:** this never stops the Music Tool's own install. The card says the app will offer to get ffmpeg.

ffmpeg is never carried inside the installer or the Music Tool's release. People get it from its makers' release, exactly as when the app fetches it. So nothing about ffmpeg's license changes for the Music Tool.

**Tell the installer's side** if any of these change: where the app keeps ffmpeg (`app_dir`, `ffmpeg_dir`), the order it looks in, or the files it keeps.
