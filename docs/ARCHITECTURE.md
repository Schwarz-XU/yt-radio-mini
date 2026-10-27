# Architecture

YT Radio Mini is a small WinForms front end around two external programs: **mpv** plays the audio and
**yt-dlp** reads playlists. The app itself does no networking and no decoding.

```
 UI (WinForms)           Application                    Services                      External
┌───────────────┐   ┌─────────────────────┐   ┌──────────────────────────┐   ┌────────────────┐
│ MainForm      │   │ RadioController     │──▶│ MpvService ─ MpvIpcClient│──▶│ mpv.exe        │
│ RadioSurface  │──▶│  (serial work loop) │   │ YoutubeResolver          │──▶│ yt-dlp.exe     │
│ LinkTextBox   │◀──│ PlaybackQueue       │   │ LinkParser               │   └────────────────┘
│ RetroTheme    │   │ QueueBrowser        │   │ StorageService           │──▶ state.json
└───────────────┘   └─────────────────────┘   │ SingleInstanceGuard      │
                                              └──────────────────────────┘
```

The UI depends only on `IRadioController`. The controller depends on three interfaces
(`IMpvService`, `IYoutubeResolver`, `IStorageService`), so everything except the WinForms code is
tested on Linux with fakes (`src/YTRadioMini/Mocks`) and, for mpv, with a real mpv process.

## Source layout

| Folder | Content |
|---|---|
| `UI/` | `MainForm` (window, keys, borderless-window handling), `RadioSurface` (all drawing and mouse handling), `LinkTextBox` (paste with normalised line breaks), `RetroTheme` (colours) |
| `Application/` | `RadioController`, `PlaybackQueue`, `QueueBrowser`, `RadioControllerOptions`. The namespace is `AppLogic` because `YTRadioMini.Application` would hide `System.Windows.Forms.Application`. |
| `Services/` | `MpvService`, `MpvIpcClient`, `YoutubeResolver`, `YtDlpPlaylistParser`, `YtDlpErrors`, `ProcessRunner`, `ChildProcessJob`, `LinkParser`, `StorageService`, `SingleInstanceGuard` |
| `Models/` | `QueueItem`, `AppState`, `PlaybackState`, `RadioViewState` |
| `Mocks/` | Fake services for `--demo` and for tests |

## Concurrency: one serial loop

`RadioController` owns all playback state. User commands, mpv events, resolver results and timers are
posted to one `Channel<Func<Task>>` that a single loop drains, so the state and the queue need no locks.
Slow work (resolving a playlist, waiting, writing the state file) happens outside the loop and posts its
result back.

Races between the UI, mpv and timers are handled by a **selection generation** `_gen` that is increased
every time "the item to play" changes. Delayed loads, the automatic skip after an error, live reconnects
and the load watchdog all capture the generation when they are created and do nothing if it has changed.
Together with these rules this keeps the queue index consistent under rapid clicking:

- Only an mpv `end-file` with reason `eof` can advance the queue, and only while the state is Playing or
  Paused. `stop` (caused by loading another file) and an `eof` that arrives while the next item is still
  loading belong to the old file and are ignored.
- Manual changes (next, previous, choosing an item) wait 120 ms before the load command is sent, so a burst
  of clicks loads only the last item.
- Three failed items in a row stop the automatic skipping.

States: `Idle, Resolving, Loading, Playing, Paused, Error, Stopped`. The UI only ever sees an immutable
`RadioViewState` snapshot (including a snapshot of the queue that is replaced only when the queue changes).

## Talking to mpv

mpv is started once with `--idle=yes --vid=no --force-window=no --terminal=no --ytdl=yes
--ytdl-format=bestaudio/best --input-ipc-server=<pipe>` (plus an ignored user config and smaller demuxer
buffers). `MpvIpcClient` speaks mpv's line-based JSON IPC over a named pipe: a read loop dispatches events
and matches replies to requests by `request_id`; writes are serialised. `MpvService` observes `pause` and
`media-title`, listens for `file-loaded` and `end-file`, remembers the last error log line to explain
failures, and decides whether a stream is live (see below). URLs are sent as JSON, never through a shell,
and only `http(s)` URLs are accepted.

The mpv and yt-dlp processes are put into a Windows Job Object with *kill on close*, so they disappear
with the app even if it is killed.

**Live detection** is a heuristic: right after a file loads, `duration` is read a few times; if it is
missing or keeps growing, the stream is treated as live.

## Links and playlists

`LinkParser` accepts only the hosts and paths listed in the README and returns canonical URLs. A link that
has both `v=` and `list=` becomes a playlist. A list id starting with `RD` (an automatic Mix, except the
`RDCLAK` album playlists) is a Mix: the seed video is queued first and the Mix is requested through the
`watch?v=...&list=RD...` URL with a limit of 25 entries, because yt-dlp would otherwise keep requesting
pages of an endless list.

`YoutubeResolver` starts yt-dlp with an argument list (no shell), a timeout and cancellation that kills the
whole process tree, and parses its single JSON document. Single videos are not resolved by yt-dlp at all;
mpv does that when the item is played.

## The state file

`%LOCALAPPDATA%\YTRadioMini\state.json`, schema version 1:

```json
{
  "schemaVersion": 1,
  "savedLinks": [ { "url": "https://youtu.be/...", "addedAt": "2026-10-27T09:00:00+00:00" } ],
  "queue": [ { "id": "VIDEO_ID", "url": "https://www.youtube.com/watch?v=VIDEO_ID", "title": "…", "isLive": false } ],
  "currentIndex": 0,
  "wasPlaying": false
}
```

It is written to `state.json.tmp` and swapped in with an atomic replace. When it is read, every queue URL
goes through `LinkParser` again (the file may have been edited), titles are cleaned and limited, and an
unusable file is renamed instead of deleted. Playback position inside a track, cookies and credentials are
never stored.

## Window

The window has no system frame. `RadioSurface` draws everything on a 360 x 240 base grid scaled by the
display DPI; the only real child control is the input box. The corners are cut with a window region of the
same radius as the drawn outline, and a click on the body starts a system window drag. Keyboard shortcuts
are handled in `ProcessCmdKey`, because arrow keys would otherwise be used for focus navigation.

## Package layout

```
YTRadioMini/
├── YTRadioMini.exe      self-contained single file (runtime and libraries inside)
├── README.txt  LICENSE.txt
├── tools/               mpv.exe, yt-dlp.exe  (found here first, then next to the exe, then on PATH)
├── docs/                THIRD-PARTY-NOTICES.txt, DEPENDENCIES.txt, CHANGELOG.md
└── licenses/            this program's licence and the licence texts of mpv, yt-dlp and .NET
```
