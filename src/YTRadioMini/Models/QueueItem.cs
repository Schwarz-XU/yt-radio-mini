namespace YTRadioMini.Models;

/// <param name="Id">YouTube 视频 ID，队列内用作稳定标识。</param>
/// <param name="Url">可直接交给 mpv 的规范化 URL。</param>
/// <param name="Title">标题；未知时为空字符串，播放后可由 mpv 的 media-title 补全。</param>
/// <param name="IsLive">已知为直播时为 true；未知时为 false。</param>
public record QueueItem(string Id, string Url, string Title, bool IsLive);
