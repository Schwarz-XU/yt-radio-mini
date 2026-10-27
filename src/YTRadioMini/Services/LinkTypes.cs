namespace YTRadioMini.Services;

public enum LinkKind
{
    Video,
    Playlist,
}

/// <param name="Kind">Video 或 Playlist。同时带 v 与 list 参数的链接按 Playlist 处理。</param>
/// <param name="Url">规范化后的 URL。</param>
/// <param name="Id">视频 ID 或播放列表 ID。</param>
/// <param name="OriginalText">用户输入的原始行，用于保存到 savedLinks。</param>
/// <param name="MixSeedVideoId">
/// 仅用于 YouTube 自动合辑（Mix，列表 ID 以 RD 开头）：种子视频的 ID。
/// 此时 Kind 为 Playlist，Url 是 watch?v=种子&amp;list=合辑 形式（合辑不能用 /playlist 地址展开）。
/// </param>
public sealed record ParsedLink(LinkKind Kind, string Url, string Id, string OriginalText, string? MixSeedVideoId = null);

/// <param name="Text">无法接受的原始输入行。</param>
/// <param name="Reason">简短原因，可显示在 LCD。</param>
public sealed record InvalidLink(string Text, string Reason);

public sealed record LinkParseResult(
    IReadOnlyList<ParsedLink> Valid,
    IReadOnlyList<InvalidLink> Invalid);
