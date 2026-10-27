using YTRadioMini.Models;

namespace YTRadioMini.Services;

public enum ResolveErrorKind
{
    Network,        // 无网络
    Unavailable,    // 私密或已删除
    GeoBlocked,     // 地区限制
    NotStarted,     // 直播尚未开始
    Timeout,
    ToolMissing,    // 找不到 yt-dlp
    ParseFailed,    // yt-dlp 无法解析或输出异常
}

public sealed record ResolveFailure(ResolveErrorKind Kind, string Message);

/// <param name="Items">解析出的条目（保持原始顺序）；失败时为空。</param>
/// <param name="Failure">失败原因；成功时为 null。</param>
public sealed record ResolveResult(IReadOnlyList<QueueItem> Items, ResolveFailure? Failure)
{
    public static ResolveResult Ok(IReadOnlyList<QueueItem> items) => new(items, null);
    public static ResolveResult Fail(ResolveErrorKind kind, string message) =>
        new(Array.Empty<QueueItem>(), new ResolveFailure(kind, message));
}

/// <summary>
/// 把一个 ParsedLink 展开为队列条目。
/// Video 不需要启动 yt-dlp（由 mpv 播放时解析），Playlist 使用 --flat-playlist。
/// 约定：预期内的失败通过 ResolveResult.Failure 返回；只有取消才抛 OperationCanceledException。
/// </summary>
public interface IYoutubeResolver
{
    Task<ResolveResult> ResolveAsync(ParsedLink link, CancellationToken ct);
}
