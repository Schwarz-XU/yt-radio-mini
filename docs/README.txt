YT Radio Mini
=============

A minimal audio player for YouTube. Paste a link and listen; no video is ever shown.
No browser, no account and no API key are needed. Your queue is saved on this computer only.

Start
-----
Double-click YTRadioMini.exe. Windows 10 or 11 (64-bit) is required. Nothing else has to be
installed. Only one copy runs at a time: starting it again brings the running one to the front.

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
If the file is damaged it is renamed to state.json.corrupt-<time> and the program starts empty.
Delete the file to start from scratch.

If videos stop playing
----------------------
YouTube changes often. If many videos suddenly fail, update yt-dlp: download the newest
yt-dlp.exe from https://github.com/yt-dlp/yt-dlp/releases and replace the yt-dlp.exe that sits
next to YTRadioMini.exe. Close YT Radio Mini first.

What is in this folder
----------------------
  YTRadioMini.exe             The program (the other .dll files next to it are required)
  mpv.exe                     The audio player engine (https://mpv.io)
  yt-dlp.exe                  Reads YouTube playlists and streams (https://github.com/yt-dlp/yt-dlp)
  DEPENDENCIES.txt            Exact versions and checksums of the bundled programs
  THIRD-PARTY-NOTICES.txt     Licences and where to find the source code

Problems
--------
  "MPV NOT FOUND" / "YT-DLP MISSING"   mpv.exe or yt-dlp.exe is missing from this folder.
  "NO NETWORK"                         The computer cannot reach YouTube.
  "VIDEO UNAVAILABLE"                  Private, deleted or members-only video.
  "REGION BLOCKED"                     The video is not available in your country.
  "LIVE NOT STARTED"                   The live stream has not begun yet.
  "SIGN-IN REQUIRED"                   YouTube asks for a login or a bot check; try again later.
