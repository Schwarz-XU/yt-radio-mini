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
}
