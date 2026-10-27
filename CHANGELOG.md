# Changelog

All notable changes to this project are documented in this file.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project
uses [Semantic Versioning](https://semver.org/). Versions below 1.0.0 may still change behaviour
between releases.

## [0.1.0] - 2026-10-27

First public release.

### Added

- **Audio-only YouTube playback** through an external `mpv` process. No video window, no browser,
  no YouTube API key, no account.
- **Links:** `watch?v=`, `youtu.be/`, `live/`, `shorts/` and `playlist?list=` links on
  `youtube.com`, `m.youtube.com`, `music.youtube.com`, `www.youtube.com` and `youtu.be`. The host,
  scheme, port and path are checked with a real URL parser; everything else is rejected with a short
  reason. Links are normalised to canonical URLs.
- **Playlists** are expanded in the background with `yt-dlp --flat-playlist` (entries only, no
  stream resolution, up to 5000 items, 60 s limit).
- **Automatic Mixes** (`list=RD...`): the linked video starts playing immediately and the first
  page of the Mix (25 entries) is appended after it, skipping videos already in the queue.
- **Queue:** several links at once (pasted lines, or Ctrl+Enter for a new line), previous / next /
  play-pause, and a queue list on the LCD to play any item, remove items, reorder them and clear the
  queue. The queue never wraps around.
- **Live streams:** a `LIVE` marker (heuristic based on mpv's `duration`) and up to two automatic
  reconnects with a short back-off when a stream drops.
- **Error handling shown on the LCD:** no network, unavailable or private video, region block, live
  stream not started, sign-in required, timeouts, yt-dlp or mpv missing, player stopped. A failed item
  is skipped after a short pause; after three failures in a row the queue stops instead of running
  through everything. A load that does not start within 45 s counts as a failure.
- **Rapid clicking is safe:** manual track changes are debounced, stale mpv events are ignored, and all
  state changes run on one serial loop.
- **Persistence:** the queue and position are saved to `%LOCALAPPDATA%\YTRadioMini\state.json`
  (temporary file plus atomic replace) and restored at start without starting playback. A damaged,
  empty or newer-format file is renamed to `state.json.corrupt-<time>` and the app starts empty.
  Restored URLs are validated again before they can reach mpv.
- **Retro radio interface**, 360 x 240 at 100% scaling: borderless window with its own minimize and
  close studs, drag by the body, rounded corners, speaker grille, three round buttons, status lamp,
  scaling with the display DPI, keyboard shortcuts (Space, N, P, L, arrows, Ctrl+V, Esc) and a
  hint text in the input box.
- **Application icon** for the exe, the window and the taskbar.
- **Only one instance** runs at a time; starting a second one brings the first to the front.
- **Clean shutdown:** closing the window stops `mpv` and `yt-dlp`; a Windows Job Object also removes
  them if the program is killed.
- **Release package:** self-contained single-file `YTRadioMini.exe` (no .NET installation needed),
  `mpv.exe` and `yt-dlp.exe` in `tools\`, notices in `docs\`, licence texts in `licenses\`.
  Bundled programs are pinned to exact versions and verified by SHA-256 (`scripts/dependencies.json`).
- **Scripts:** `fetch-deps.ps1`, `build-release.ps1`, `measure-performance.ps1`, `make-icon.py`.
- **Documentation:** README, end-user README, architecture notes, third-party notices, test report.
- 280 automated tests (parser, queue, storage, yt-dlp handling, mpv IPC against a fake mpv and a
  real mpv, controller, single instance).

### Known limitations

- Needs Windows 10 or 11 (64-bit). The window cannot be resized or maximised.
- Not verified on real YouTube yet: the `LIVE` marker, live reconnect after a network drop, most error
  messages, very long playlists, and display scaling across monitors with different DPI.
- yt-dlp may need a JavaScript runtime (Deno is the one it recommends) for full YouTube support; none
  is bundled. If many videos fail, install Deno or replace `tools\yt-dlp.exe` with a newer release.
- The executable is not code-signed, so Windows SmartScreen may warn on first start.
- No volume control, progress bar, search, downloads or accounts, by design.

[0.1.0]: https://github.com/Schwarz-XU/yt-radio-mini/releases/tag/v0.1.0
