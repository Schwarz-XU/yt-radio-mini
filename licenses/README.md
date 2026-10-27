# Licence texts shipped with the release package

These files are copies of the licence texts of the programs that are bundled in the release
package. They are included so that the package is self-contained; they are not part of the
YT Radio Mini source code (whose licence is in `LICENSE`, `COPYING.LESSER` and `COPYING`).

| File | What it is | Taken from (retrieved 2026-10-27) |
|---|---|---|
| `mpv/Copyright` | mpv's statement of how it is licensed | https://github.com/mpv-player/mpv/blob/77e8f9cfa1/Copyright |
| `mpv/LICENSE.GPL` | GNU GPL version 2 | https://github.com/mpv-player/mpv/blob/77e8f9cfa1/LICENSE.GPL |
| `mpv/LICENSE.LGPL` | GNU LGPL version 2.1 | https://github.com/mpv-player/mpv/blob/77e8f9cfa1/LICENSE.LGPL |
| `yt-dlp/LICENSE` | The Unlicense (yt-dlp's own code) | https://github.com/yt-dlp/yt-dlp/blob/2026.10.26/LICENSE |
| `yt-dlp/THIRD_PARTY_LICENSES.txt` | Licences of the components inside the PyInstaller-built `yt-dlp.exe` | https://github.com/yt-dlp/yt-dlp/blob/2026.10.26/THIRD_PARTY_LICENSES.txt |
| `dotnet/LICENSE.TXT` | MIT licence of the .NET runtime and Windows Forms | https://github.com/dotnet/runtime/blob/v10.0.13/LICENSE.TXT |
| `dotnet/THIRD-PARTY-NOTICES.TXT` | Notices for third-party code inside the .NET runtime | https://github.com/dotnet/runtime/blob/v10.0.13/THIRD-PARTY-NOTICES.TXT |
| `GPL-3.0.txt` | GNU GPL version 3 (also copied to `COPYING` in the repository root) | https://github.com/spdx/license-list-data/blob/main/text/GPL-3.0-or-later.txt |

`77e8f9cfa1` is the mpv commit that the bundled `mpv.exe` reports in its version string
(`mpv v0.41.0-1317-g77e8f9cfa`). The licence texts of mpv were taken from that commit; the
.NET files from the tag of the bundled runtime version. When a bundled program is updated in
`scripts/dependencies.json`, refresh the matching files here.
