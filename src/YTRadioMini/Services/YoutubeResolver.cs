using YTRadioMini.Models;

namespace YTRadioMini.Services;

/// <summary>
/// 把 ParsedLink 展开为队列条目。
/// 单个视频不启动 yt-dlp（播放时由 mpv 解析）；播放列表用 --flat-playlist 只取条目，不解析媒体流。
/// </summary>
public sealed class YoutubeResolver : IYoutubeResolver
{
    public const int MaxPlaylistItems = 5000;
    private const int MaxJsonChars = 64 * 1024 * 1024;

    public delegate Task<ProcessResult> Runner(string fileName, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct);

    private readonly string _ytDlpPath;
    private readonly TimeSpan _timeout;
    private readonly Runner _run;

    public YoutubeResolver(string ytDlpPath, TimeSpan? timeout = null, Runner? runner = null)
    {
        _ytDlpPath = ytDlpPath;
        _timeout = timeout ?? TimeSpan.FromSeconds(60);
        _run = runner ?? DefaultRunner;
    }

    public async Task<ResolveResult> ResolveAsync(ParsedLink link, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (link.Kind == LinkKind.Video)
            return ResolveResult.Ok(new[] { new QueueItem(link.Id, link.Url, "", false) });

        var args = new[]
        {
            "--ignore-config",
            "--flat-playlist",
            "--dump-single-json",
            "--no-download",
            "--no-warnings",
            "--ignore-errors",
            "--playlist-end", MaxPlaylistItems.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--",
            link.Url,
        };

        var result = await _run(_ytDlpPath, args, _timeout, ct).ConfigureAwait(false);

        if (result.StartFailed)
            return ResolveResult.Fail(ResolveErrorKind.ToolMissing, YtDlpErrors.ShortMessage(ResolveErrorKind.ToolMissing));

        if (result.TimedOut)
            return ResolveResult.Fail(ResolveErrorKind.Timeout, YtDlpErrors.ShortMessage(ResolveErrorKind.Timeout));

        // --ignore-errors 下个别条目失败时退出码也可能非 0，所以只要输出可解析就采用。
        if (!string.IsNullOrWhiteSpace(result.StdOut))
        {
            var parsed = YtDlpPlaylistParser.Parse(result.StdOut);
            if (parsed.Failure is null) return parsed;
            if (result.ExitCode == 0) return parsed;
        }

        var kind = YtDlpErrors.Classify(result.StdErr);
        return ResolveResult.Fail(kind, YtDlpErrors.ShortMessage(kind));
    }

    private static Task<ProcessResult> DefaultRunner(string file, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct) =>
        ProcessRunner.RunAsync(file, args, timeout, MaxJsonChars, ct);
}
