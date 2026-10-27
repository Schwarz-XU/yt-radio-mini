YT Radio Mini 0.1.0
===================

A minimal audio player for YouTube. Paste a link and listen; no video is ever shown.
No browser, no account and no API key are needed. Your queue is saved on this computer only.

Start
-----
Double-click YTRadioMini.exe. Windows 10 or 11 (64-bit) is required. Nothing else has to be
installed. Only one copy runs at a time: starting it again brings the running one to the front.

The program is not code-signed, so Windows SmartScreen may say "Windows protected your PC" the
first time. Choose "More info", then "Run anyway". If you downloaded a .zip file, right-click it,
open Properties and tick "Unblock" before you extract it; that avoids the warning for every file.

Adding music
------------
Paste a YouTube link into the box at the bottom and press Enter.

  Supported:  youtube.com/watch?v=...   youtu.be/...   youtube.com/live/...
              youtube.com/shorts/...    youtube.com/playlist?list=...
  Playlists are expanded into the queue. A video that comes with an automatic Mix
  (list=RD...) plays at once and the first 25 entries of its Mix are added after it.

To add several links at once: paste several lines, or press Ctrl+Enter after each link to start
a new line, then press Enter. A note above the box shows how many links are waiting.
Adding links never interrupts what is playing.

Controls
--------
  Left / middle / right round button    Previous / Play-Pause / Next
  P, Space, N                           The same three actions (when the box is not focused)
  Click the screen, or press L / Up / Down
                                        Open the queue list
  Ctrl+V                                Paste into the box, even when it is not focused
  Esc, or click the radio body          Take the keyboard focus out of the box
  Drag the radio body                   Move the window
  Small studs at the top right          Minimize and close

Queue list (click the screen to open it)
----------------------------------------
  Up / Down, Page Up / Page Down, Home / End, mouse wheel   Move the cursor
  Enter, or double-click a row                              Play the selected item from the start
  Delete                                                    Remove the selected item
  Ctrl+Up / Ctrl+Down                                       Move the selected item up / down
  Shift+Delete                                              Clear the whole queue (no confirmation)
  Esc, L, or click the list header                          Back to the normal display

The lamp: green = playing, amber = loading, grey = idle, paused, stopped or error.

Your data
---------
The queue and the current position are stored in
  %LOCALAPPDATA%\YTRadioMini\state.json
and restored the next time you start the program (playback does not start by itself).
This package does not contain any queue: the first start on a computer is empty. If you see a queue
you did not add on this computer, it comes from an earlier run of the program.

To start from an empty queue, press Shift+Delete in the queue list, or close the program and delete
state.json. If the file is damaged, it is renamed to state.json.corrupt-<time> and the program starts
empty.

If videos stop playing
----------------------
YouTube changes often.
 1. Update yt-dlp: download the newest yt-dlp.exe from https://github.com/yt-dlp/yt-dlp/releases and
    replace tools\yt-dlp.exe with it. Close YT Radio Mini first.
 2. yt-dlp's documentation says that a JavaScript runtime (Deno is the recommended one) is needed for
    full YouTube support. None is included in this package. If many videos still fail after updating,
    install Deno (https://deno.com) so that "deno" can be found on the PATH, then restart the program.

What is in this folder
----------------------
  YTRadioMini.exe             The program (the .NET runtime is inside it)
  README.txt                  This file
  LICENSE.txt                 Licence of YT Radio Mini (LGPL-3.0-or-later)
  tools\mpv.exe               The audio player engine (https://mpv.io)
  tools\yt-dlp.exe            Reads YouTube playlists and streams (https://github.com/yt-dlp/yt-dlp)
  docs\THIRD-PARTY-NOTICES.txt  Licences and where to find the source code of the bundled programs
  docs\DEPENDENCIES.txt       Exact versions and checksums of the bundled programs
  docs\CHANGELOG.md           What changed in each version
  licenses\                   The licence texts

Problems
--------
  "MPV NOT FOUND" / "YT-DLP MISSING"   mpv.exe or yt-dlp.exe is missing from the tools folder.
  "NO NETWORK"                         The computer cannot reach YouTube.
  "VIDEO UNAVAILABLE"                  Private, deleted or members-only video.
  "REGION BLOCKED"                     The video is not available in your country.
  "LIVE NOT STARTED"                   The live stream has not begun yet.
  "SIGN-IN REQUIRED"                   YouTube asks for a login or a bot check; try again later.
  "TIMED OUT"                          Loading took longer than 45 seconds.
  "PLAYER STOPPED" / "PLAYER ERROR"    mpv ended unexpectedly; press play to start it again.

Licence
-------
YT Radio Mini: Copyright (C) 2026 Schwarz-XU, GNU Lesser General Public License version 3 or later.
mpv, yt-dlp and the .NET runtime are separate programs under their own licences; mpv.exe and
yt-dlp.exe are GPL-licensed programs. See docs\THIRD-PARTY-NOTICES.txt for details and for where to
get their source code.
