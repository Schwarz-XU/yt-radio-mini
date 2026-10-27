# YT Radio Mini

A minimal retro Windows audio player for YouTube: paste a link and listen. It never shows video, needs
no browser, no account and no API key. Built with C# / .NET 10 / WinForms; playback is done by an external
[mpv](https://mpv.io) and playlists are read with [yt-dlp](https://github.com/yt-dlp/yt-dlp).

> **Version 0.1.0**, the first public release. Automated tests pass; what has and has not been verified
> on real Windows machines is listed in [docs/TEST-REPORT.md](docs/TEST-REPORT.md).

## Features

- Plays the audio of YouTube videos, live streams, Shorts, playlists and automatic Mixes.
- A queue you can build from several links at once and edit: previous, next, jump to any item, remove,
  reorder, clear. It is saved and restored at the next start (without starting playback).
- Small and quiet: about 15 MB for the window and about 54 MB for mpv while playing, CPU close to zero
  (figures reported by the project owner on one computer; see the test report).
- One 360 x 240 window with its own title-less retro frame; one instance at a time.
- Nothing is uploaded: the queue is stored only in `%LOCALAPPDATA%\YTRadioMini\state.json`.

## Download and run

Get `YTRadioMini-0.1.0-win-x64.zip` from the
[Releases](https://github.com/Schwarz-XU/yt-radio-mini/releases) page, unblock and extract it, and start
`YTRadioMini.exe`. Windows 10 or 11 (64-bit) is required; no .NET installation is needed. The program is not
code-signed, so SmartScreen may warn on the first start. The package folder looks like this:

```
YTRadioMini/
├── YTRadioMini.exe     the program (self-contained, the .NET runtime is inside)
├── README.txt          user guide
├── LICENSE.txt
├── tools/              mpv.exe, yt-dlp.exe
├── docs/               third-party notices, versions and checksums, changelog
└── licenses/           licence texts
```

The same guide as `README.txt` is in [docs/README.txt](docs/README.txt).

## Supported links

| Link | Result |
|---|---|
| `youtube.com/watch?v=ID`, `youtu.be/ID`, `youtube.com/live/ID`, `youtube.com/shorts/ID` | One video or live stream |
| `youtube.com/playlist?list=ID` | The playlist, expanded into the queue |
| `watch?v=ID&list=ID` (a video inside a playlist) | The playlist |
| A video with an automatic Mix (`list=RD…`) | The video plays at once, then the Mix's first page (25 entries) is appended after it; videos already in the queue are skipped. If the Mix cannot be expanded, only the video plays and the LCD shows `MIX: <reason>`. |

Lines that are not supported YouTube links are skipped and counted on the LCD. Only `youtube.com`, `www.`,
`m.`, `music.` and `youtu.be` are accepted.

## Controls

The shortcuts work whenever the input box does **not** have keyboard focus. Press Esc or click the radio body
to take the focus away from the input box.

| Action | Mouse | Key |
|---|---|---|
| Previous item | Left round button | P |
| Play / pause | Middle round button | Space |
| Next item | Right round button | N |
| Open the queue list | Click the LCD | L, or Up / Down / Page Up / Page Down |
| Paste into the input box | | Ctrl+V (works even when the box is not focused) |
| Move the window | Drag the radio body | |
| Minimize / close | The two small studs at the top right | |

At the start or end of the queue the Previous or Next button is disabled; the queue never wraps around.

**Adding links.** Type or paste a link into the input box and press **Enter**. This adds everything in the box to
the end of the queue and does not interrupt what is playing. If the queue was empty or stopped, the first new
item starts playing. To add several links at once, paste several lines, or press **Ctrl+Enter** after each link to
start a new line, then press **Enter** to add them all. The box shows one line at a time; while it holds more than
one line, a note above it shows how many links are waiting.

**Queue list.** Open it by clicking the LCD or pressing L / Up / Down. The LCD then shows four queue rows, with `>`
marking the item that is playing.

| Action | Mouse | Key |
|---|---|---|
| Move the cursor | Click a row, or the mouse wheel | Up / Down, Page Up / Page Down, Home / End |
| Play the selected item (from the beginning) | Double-click a row | Enter |
| Remove the selected item | | Delete |
| Move the selected item up / down | | Ctrl+Up / Ctrl+Down |
| Clear the whole queue and stop | | Shift+Delete (no confirmation) |
| Close the list | Click the list header | Esc or L |

Removing the item that is playing starts the item that follows it; if there is none, playback stops. Moving or
removing other items does not interrupt playback. The lamp: green = playing, amber = loading, grey = idle, paused,
stopped or error.

## Building from source

Requires the .NET 10 SDK.

```bash
dotnet build YTRadioMini.sln -c Release
dotnet test tests/YTRadioMini.Tests -c Release
```

To run from source on Windows, put `mpv.exe` and `yt-dlp.exe` in the build output folder or in a folder on the `PATH`
(see [scripts/dependencies.json](scripts/dependencies.json) for the pinned versions), then:

```powershell
dotnet run --project src/YTRadioMini            # real player
dotnet run --project src/YTRadioMini -- --demo  # interface only, fake tracks, no mpv or yt-dlp needed
```

The test project links the source folders that do not depend on WinForms, so the tests also run on Linux.
`MpvServiceIntegrationTests` use a real `mpv` when it is on the `PATH`; without it they return without running.

### Release package

On Windows, with the .NET 10 SDK installed:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-release.ps1
```

This downloads `mpv.exe` and `yt-dlp.exe` as pinned in `scripts/dependencies.json` (each download is checked
against its SHA-256), runs the tests, publishes a self-contained single-file win-x64 build and creates
`artifacts/YTRadioMini-<version>-win-x64.zip`. Switches: `-SkipTests`, `-SkipZip`. Extracting the mpv archive needs
7-Zip or the `tar.exe` that comes with Windows 10/11.

To measure start-up time, memory and CPU (summed over the window, mpv and yt-dlp):

```powershell
powershell -ExecutionPolicy Bypass -File scripts/measure-performance.ps1 -Url "https://www.youtube.com/watch?v=..." -ReportPath perf.md
```

| Folder | Content |
|---|---|
| `src/YTRadioMini` | The program: `UI`, `Application`, `Services`, `Models`, `Mocks` ([architecture](docs/ARCHITECTURE.md)) |
| `tests/YTRadioMini.Tests` | Unit and integration tests |
| `scripts` | Dependency download, release build, performance measurement, icon generator |
| `docs` | User guide, architecture, third-party notices, test report, release notes |
| `licenses` | Licence texts of the programs bundled in the release package |
| `assets` | `app.ico`, generated by `scripts/make-icon.py` |

## Documentation

- [CHANGELOG.md](CHANGELOG.md): what changed in each version
- [docs/README.txt](docs/README.txt): the user guide that ships in the package
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): how it works
- [docs/TEST-REPORT.md](docs/TEST-REPORT.md): tests and what is still unverified
- [docs/THIRD-PARTY-NOTICES.txt](docs/THIRD-PARTY-NOTICES.txt): licences of mpv, yt-dlp and .NET, and where their source code is

## Licence

YT Radio Mini is Copyright (C) 2026 Schwarz-XU and licensed under the **GNU Lesser General Public License,
version 3 or later** ([LICENSE](LICENSE), [COPYING.LESSER](COPYING.LESSER), [COPYING](COPYING)).

The release package also contains **mpv** and **yt-dlp**, which are separate programs started by YT Radio Mini
(it does not link to them). Both are GPL-licensed programs; so is the combined `yt-dlp.exe`, and the bundled
`mpv.exe` is a GPL build. Their licences and the addresses of their source code are in
[docs/THIRD-PARTY-NOTICES.txt](docs/THIRD-PARTY-NOTICES.txt). The .NET runtime inside `YTRadioMini.exe` is under the
MIT licence. This section is information, not legal advice.

## About this project

YT Radio Mini is developed by Schwarz-XU with the help of Claude (Anthropic) working as a coding agent; commits that
Claude wrote carry a `Co-Authored-By` trailer.
