namespace YTRadioMini.AppLogic;

public sealed class RadioControllerOptions
{
    /// <summary>条目播放失败后，在 LCD 上显示错误多久再自动跳到下一项。</summary>
    public TimeSpan ErrorSkipDelay { get; init; } = TimeSpan.FromMilliseconds(1500);

    /// <summary>连续失败达到这个数量后停在错误状态，不再自动跳过（防止断网时把整个队列刷完）。</summary>
    public int MaxConsecutiveFailures { get; init; } = 3;

    /// <summary>直播异常断流后的自动重试等待时间；元素个数即最多重试次数。</summary>
    public IReadOnlyList<TimeSpan> LiveRetryDelays { get; init; } = new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3) };

    /// <summary>发出加载命令后，超过这个时间仍没有开始播放即视为失败。</summary>
    public TimeSpan LoadTimeout { get; init; } = TimeSpan.FromSeconds(45);

    /// <summary>队列或位置变化后，延迟多久写入 state.json（合并连续变化）。</summary>
    public TimeSpan SaveDebounce { get; init; } = TimeSpan.FromMilliseconds(500);

    public int MaxSavedLinks { get; init; } = 200;
}
