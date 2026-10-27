# YT Radio Mini 0.1.0: test report

Status at the time of writing: **first public release (0.1.0).** Automated tests pass; the real-world
checks are partly done by the project owner and partly still open (see below).

## 1. Automated tests

Run on Linux (x64) with the .NET SDK 10.0.113; `mpv` 0.37 was installed for the integration tests.
Windows-only code (WinForms, the Windows Job Object, the window-region corners, the Windows named-pipe path of
mpv) is **not** exercised by them.

Result: **280 passed, 0 failed, 0 skipped** (`dotnet test tests/YTRadioMini.Tests -c Release`, about 45 s).
The controller tests were repeated several times in a row without a failure.

| Area | Tests | What is covered |
|---|---:|---|
| `LinkParser` | 72 | Accepted link forms and canonical URLs; look-alike hosts, user-info, ports, other schemes and option-like input rejected; playlist vs. video precedence; Mix and Shorts links |
| `PlaybackQueue` | 35 | Append, restore, next/previous without wrapping, remove, move and clear keep the current item consistent |
| `QueueBrowser` | 10 | Cursor and scroll window of the queue list |
| `StorageService` | 20 | Round trip; atomic replace; concurrent saves; damaged, empty, newer-schema files backed up; restored URLs re-validated; limits |
| yt-dlp (`YtDlpPlaylistParser`, `YtDlpErrors`, `YoutubeResolver`, `ProcessRunner`) | 38 | Playlist JSON parsing (the JSON samples are hand-written, not captured from YouTube); error classification; arguments passed without a shell; Mix limit; timeout and cancellation kill the process |
| `MpvIpcClient` | 11 | IPC protocol against a scripted fake mpv over a real named pipe: request ids, out-of-order replies, events, bad lines, disconnect, timeout |
| `MpvService` with a real mpv | 17 | Loading, events, pause, stop vs. end of file, errors, live-stream heuristic, restart after the process was killed, no leftover process after dispose |
| `RadioController` | 67 | Playback flow, rapid Next, stale events, failures and the three-failures limit, live reconnect, load timeout, queue editing, Mix expansion, persistence, shutdown, a 1500-operation random-interleaving test |
| `SingleInstanceGuard` | 6 | Only the first instance acquires; the second can ask the first to come to the front |
| Other | 4 | Early smoke tests |

## 2. Checks done on real Windows machines

Reported by the project owner during development (the exact builds and links were not recorded):

- Normal video links, live-stream links, playlists, a video with a Mix (first page of the Mix is appended),
  Shorts links, multi-line input, queue list editing: **worked**.
- Closing the window leaves no `mpv.exe` or `yt-dlp.exe` behind: **verified**.
- After a restart the queue and position are restored: **verified**.
- A first look at the borderless window (screenshot): the layout, colours, lamp, buttons and queue/LCD text render as designed.
- Resource use while playing, as reported: GUI about 15 MB, mpv about 54 MB, CPU close to 0.
  Which memory figure was read (Task Manager "Memory" or working set), the Windows version and whether
  `yt-dlp` was running were not stated. Use `scripts/measure-performance.ps1` for a repeatable number.

## 3. Not yet verified

| Item | Why it matters |
|---|---|
| The borderless window, window buttons, dragging, the application icon and the rounded corners | Added after the last real-world test. The owner's first look at the borderless window showed a second, smaller corner radius behind the drawn one; the window shape and the drawn outline now use the same radius, which has not been looked at on Windows yet |
| The single-file package layout (`YTRadioMini.exe` with the runtime inside, tools in `tools\`) | Built and inspected on Linux only; start-up time of the single-file exe on Windows is unknown |
| Single-instance start-up on Windows (second start brings the first window to the front) | Unit-tested through the shared code; the foreground switch itself is Windows-only |
| Killing `YTRadioMini.exe` from Task Manager takes `mpv.exe`/`yt-dlp.exe` with it | Relies on a Windows Job Object that has not been run on Windows |
| `LIVE` marker on a real YouTube live stream | Heuristic based on mpv's `duration`; tested only with local streams |
| Live-stream reconnect after a network drop | Logic is unit-tested with fake events |
| Private / deleted / region-blocked / not-yet-started videos and offline start-up | Error classification is tested on sample messages, not on real yt-dlp output |
| Playlists of several hundred items | Limit is 5000 entries and 60 s |
| Moving the window between monitors with different scaling | Layout scales from a 360 x 240 base but has not been seen on such a setup |
| Start-up time | Not measured |

## 4. Known limitations

- YouTube changes often. If many videos fail at once, replace `yt-dlp.exe` with a newer one.
- A YouTube Mix is endless and different on every request; only its first page (25 entries when checked) is used.
- The bundled `mpv.exe` is pinned by the SHA-256 computed when it was first downloaded; its publisher
  provides no checksum file. `yt-dlp.exe` matches the checksum file of its release.
- The window cannot be resized or maximized. There is no volume control, progress bar or search, by design.
