# YT Radio Mini 0.1.0

First public release: a minimal retro Windows audio player for YouTube. Paste a link, listen; no video,
no browser, no account.

## Highlights

- Videos, live streams, Shorts, playlists and automatic Mixes (the Mix's first 25 entries are appended after
  the video you linked).
- A queue you can edit on the radio's own screen: previous, next, jump to an item, remove, reorder, clear.
  Saved and restored at the next start (without starting playback).
- Retro 360 x 240 window with its own minimize and close studs, one instance at a time.
- Errors are shown on the LCD in a few words; failed items are skipped; live streams reconnect up to twice.

See [CHANGELOG.md](https://github.com/Schwarz-XU/yt-radio-mini/blob/main/CHANGELOG.md) for the full list.

## Download

`YTRadioMini-0.1.0-win-x64.zip`: Windows 10 / 11, 64-bit. Nothing else needs to be installed.
Unblock the zip (right-click, Properties, "Unblock") before extracting, then start `YTRadioMini.exe`.
The program is not code-signed, so SmartScreen may warn on the first start ("More info", then "Run anyway").

SHA-256 of the zip: `9961be290d845048ebd35e2f04ab95ac36f1b4ddbe6703c5856a62da03a5c720`

## What is inside

| Path | Content |
|---|---|
| `YTRadioMini.exe` | The program, with the .NET runtime inside |
| `tools/mpv.exe` | mpv v0.41.0-1317-g77e8f9cfa, Windows build by [zhongfly](https://github.com/zhongfly/mpv-winbuild) |
| `tools/yt-dlp.exe` | yt-dlp 2026.10.26 |
| `docs/`, `licenses/` | Notices, versions and checksums, changelog, licence texts |

Exact checksums of the bundled programs are in `docs/DEPENDENCIES.txt`.

## Licences

- YT Radio Mini: LGPL-3.0-or-later.
- `mpv.exe` is a **GPL** build (GPL v2 or later) and `yt-dlp.exe` is, as a whole, under the **GPL v3 or later**
  (yt-dlp's own code is the Unlicense). They are separate programs that YT Radio Mini starts; it does not link to
  them. Their licence texts and the addresses of their source code are in `docs/THIRD-PARTY-NOTICES.txt` and
  `licenses/`. If you cannot get the source code of `mpv.exe` or `yt-dlp.exe` from those addresses, open an issue
  and ask for it.

## Known limitations

- Not yet verified on real YouTube: the `LIVE` marker, live reconnect, most error messages, very long playlists,
  and display scaling across monitors with different DPI. See `docs/TEST-REPORT.md`.
- yt-dlp may need a JavaScript runtime (Deno is recommended) for full YouTube support; none is bundled. If many
  videos fail, install Deno or replace `tools/yt-dlp.exe` with a newer release.
- No volume control, progress bar, search or downloads, by design.
