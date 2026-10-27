namespace YTRadioMini.Services;

/// <summary>mpv end-file 事件的 reason 字段。</summary>
public enum MpvEndReason
{
    Eof,       // 自然播放结束 —— 只有这一种才允许自动切下一首
    Stop,      // loadfile replace 或 stop 导致的结束，手动切换时会出现，不得触发下一首
    Quit,
    Error,
    Redirect,
    Unknown,
}

public sealed record MpvEndFile(MpvEndReason Reason, string? ErrorText);

/// <summary>
/// 外部 mpv 进程的封装。
/// 约定：
/// 1. 所有事件都在后台线程触发，订阅者自行切换线程，且处理函数不得阻塞。
/// 2. 异步方法不得在 UI 线程上同步等待管道。
/// 3. 进程意外退出时触发 <see cref="ProcessExited"/>；调用方可再次 StartAsync。
/// </summary>
public interface IMpvService : IAsyncDisposable
{
    event Action? FileLoaded;
    event Action<MpvEndFile>? EndFile;
    event Action<bool>? PauseChanged;
    event Action<string>? MediaTitleChanged;
    event Action<string>? ProcessExited;

    /// <summary>
    /// 文件加载后的数秒内判断出是否为直播（启发式，依据 mpv 的 duration 属性）。
    /// 每个文件至多触发一次，且只在该文件仍是当前文件时触发；true 会比 false 早出现。
    /// </summary>
    event Action<bool>? LiveDetected;

    /// <summary>启动 mpv 并连接 IPC。重复调用在已运行时不做任何事。</summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>loadfile URL replace。URL 经 IPC JSON 传递，不经过 shell。</summary>
    Task LoadAsync(string url, CancellationToken ct);

    Task SetPauseAsync(bool paused, CancellationToken ct);

    /// <summary>停止当前播放，保留 mpv 进程。</summary>
    Task StopAsync(CancellationToken ct);
}
