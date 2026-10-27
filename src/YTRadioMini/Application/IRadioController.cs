using YTRadioMini.Models;

namespace YTRadioMini.AppLogic;

/// <summary>
/// UI 唯一依赖的入口。UI 不得直接引用 mpv 或 yt-dlp 相关类型。
/// 约定：
/// 1. <see cref="StateChanged"/> 可能在任意线程触发，UI 需自行切换到 UI 线程。
/// 2. 所有方法立即返回的可等待任务不会阻塞 UI 线程；失败通过 State.Message 反映，不抛给 UI。
/// </summary>
public interface IRadioController : IAsyncDisposable
{
    RadioViewState State { get; }
    event Action<RadioViewState>? StateChanged;

    /// <summary>启动时调用：恢复上次队列和位置，默认不自动播放。</summary>
    Task RestoreAsync(CancellationToken ct = default);

    /// <summary>提交输入框内容（单行或多行），追加到队列。</summary>
    Task SubmitAsync(string text);

    Task TogglePauseAsync();
    Task NextAsync();
    Task PreviousAsync();

    // 队列编辑。索引都是 0 起始的队列位置；越界的请求被忽略。

    /// <summary>立即播放指定位置的条目（正在播放的条目也会从头重新开始）。</summary>
    Task PlayIndexAsync(int index);

    /// <summary>删除条目。删除正在播放的条目时，原来的下一项顶上来并开始播放；没有下一项则停止。</summary>
    Task RemoveAsync(int index);

    /// <summary>把条目从 from 移到 to，不影响正在播放的内容。</summary>
    Task MoveAsync(int from, int to);

    /// <summary>清空队列并停止播放。</summary>
    Task ClearAsync();
}
