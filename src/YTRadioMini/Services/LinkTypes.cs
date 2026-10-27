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
public sealed record ParsedLink(LinkKind Kind, string Url, string Id, string OriginalText);

/// <param name="Text">无法接受的原始输入行。</param>
/// <param name="Reason">简短原因，可显示在 LCD。</param>
public sealed record InvalidLink(string Text, string Reason);

public sealed record LinkParseResult(
    IReadOnlyList<ParsedLink> Valid,
    IReadOnlyList<InvalidLink> Invalid);
