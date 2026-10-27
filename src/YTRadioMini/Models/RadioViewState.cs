namespace YTRadioMini.Models;

/// <summary>
/// RadioController 推送给 UI 的只读快照。UI 的全部显示内容都由它推导，
/// UI 自己不保存播放状态。
/// </summary>
/// <param name="State">当前播放状态。</param>
/// <param name="Title">LCD 标题；没有内容时为空字符串。</param>
/// <param name="Position">队列位置，从 1 开始；队列为空时为 0。</param>
/// <param name="Count">队列总数。</param>
/// <param name="IsLive">当前条目是否为直播（LCD 显示 LIVE）。</param>
/// <param name="Message">LCD 上的简短错误或提示；无则为 null。</param>
/// <param name="CanTogglePause">播放/暂停按钮是否可用。</param>
/// <param name="CanNext">下一首按钮是否可用（队列末尾时为 false）。</param>
public sealed record RadioViewState(
    PlaybackState State,
    string Title,
    int Position,
    int Count,
    bool IsLive,
    string? Message,
    bool CanTogglePause,
    bool CanNext)
{
    public static RadioViewState Empty { get; } =
        new(PlaybackState.Idle, "", 0, 0, false, null, false, false);
}
