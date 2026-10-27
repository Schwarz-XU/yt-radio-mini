# YT Radio Mini

A minimal Windows audio player for YouTube: paste a link and listen. It never shows video. Built with C# / .NET 10 / WinForms; playback is done by an external `mpv`, and playlists are expanded with `yt-dlp`.

> Development build. The modules are implemented and unit-tested, but the release package, the performance measurements and the full real-world YouTube verification are still to do.

## Running it on Windows (development)

Requires the .NET 10 SDK.

1. Put these two programs in one of the following places:
   - the build output folder (next to `YTRadioMini.exe`, for example `src\YTRadioMini\bin\Debug\net10.0-windows\`), or
   - any folder on the system `PATH`.

   | File | Where to get it |
   |---|---|
   | `yt-dlp.exe` | https://github.com/yt-dlp/yt-dlp/releases. Verify it with the `SHA2-256SUMS` file of the same release. |
   | `mpv.exe` | A Windows build listed at https://mpv.io/installation/ |

2. Start the app:

   ```powershell
   dotnet run --project src/YTRadioMini
   ```

   To look at the interface only, without starting mpv or yt-dlp:

   ```powershell
   dotnet run --project src/YTRadioMini -- --demo
   ```

   In `--demo` mode every submitted text adds three fake tracks; text containing `bad` shows an error.

## Supported links

| Link | Result |
|---|---|
| `youtube.com/watch?v=ID`, `youtu.be/ID`, `youtube.com/live/ID`, `youtube.com/shorts/ID` | One video or live stream |
| `youtube.com/playlist?list=ID` | The playlist, expanded into the queue |
| `watch?v=ID&list=ID` (a video inside a playlist) | The playlist |
| A video with an automatic Mix (`list=RD…`) | The video starts playing right away, then the Mix is expanded and appended after it. If the Mix cannot be expanded, only the video plays. |

Lines that are not supported YouTube links are skipped and counted on the LCD. Only `youtube.com`, `www.`, `m.`, `music.` and `youtu.be` are accepted.

## Controls

### Buttons and keys

The shortcuts below work whenever the input box does **not** have keyboard focus. Press Esc or click the radio body to take the focus away from the input box.

| Action | Mouse | Key |
|---|---|---|
| Previous item | Left round button | P |
| Play / pause | Middle round button | Space |
| Next item | Right round button | N |
| Open the queue list | Click the LCD | L, or Up / Down / Page Up / Page Down |
| Paste into the input box | | Ctrl+V (works even when the input box is not focused) |

At the start or end of the queue the Previous or Next button is disabled; the queue never wraps around.

### Adding links

Type or paste a link into the input box and press **Enter**. This adds everything in the box to the end of the queue and does not interrupt what is playing. If the queue was empty or stopped, the first new item starts playing.

To add several links at once, either paste several lines, or press **Ctrl+Enter** after each link to start a new line, then press **Enter** to add them all. The input box shows one line at a time; while it holds more than one line, a note above it shows how many links are waiting. Shift+Enter behaves like Enter.

### Queue list

Open the list by clicking the LCD or pressing L / Up / Down. The LCD then shows four queue rows, with `>` marking the item that is playing.

| Action | Mouse | Key |
|---|---|---|
| Move the cursor | Click a row, or use the mouse wheel | Up / Down, Page Up / Page Down, Home / End |
| Play the selected item (from the beginning) | Double-click a row | Enter |
| Remove the selected item | | Delete |
| Move the selected item up / down | | Ctrl+Up / Ctrl+Down |
| Clear the whole queue and stop | | Shift+Delete (no confirmation) |
| Close the list | Click the list header | Esc or L |

Removing the item that is playing starts the item that follows it; if there is none, playback stops. Moving or removing other items does not interrupt playback.

### The lamp

Grey: idle, paused, stopped or error. Green: playing. Amber: loading.

## Saved state

The queue and the current position are saved to `%LOCALAPPDATA%\YTRadioMini\state.json` and restored at the next start. Playback does not start automatically. A damaged file is renamed to `state.json.corrupt-<timestamp>` (the three most recent are kept) and the app starts with an empty queue.

## Development and tests

```bash
dotnet build YTRadioMini.sln -c Release
dotnet test tests/YTRadioMini.Tests -c Release
```

The test project links the source folders that do not depend on WinForms (`Models`, `Services`, `Application`, `Mocks`), so the tests run on Linux as well. `MpvServiceIntegrationTests` use a real `mpv` process when `mpv` is found on `PATH`; when it is not found those tests return without running and are reported as passed.

| Folder | Content |
|---|---|
| `src/YTRadioMini/UI` | Window and custom-drawn surface; depends only on `IRadioController` |
| `src/YTRadioMini/Application` | `RadioController` (serial work loop), `PlaybackQueue`, `QueueBrowser` |
| `src/YTRadioMini/Services` | `MpvService` / `MpvIpcClient`, `YoutubeResolver`, `LinkParser`, `StorageService` |
| `src/YTRadioMini/Mocks` | Fake services for the demo mode and for tests |
