using YTRadioMini.Services;

namespace YTRadioMini.Mocks;

/// <summary>
/// 记录收到的命令，并允许测试代码手动触发 mpv 事件。
/// 与真实 mpv 一致的是：SetPauseAsync 成功后会触发 PauseChanged。
/// </summary>
public sealed class FakeMpvService : IMpvService
{
    private readonly object _gate = new();
    private readonly List<string> _commands = new();

    /// <summary>已收到的命令副本，形如 "start"、"load URL"、"pause True"、"stop"。</summary>
    public IReadOnlyList<string> Commands { get { lock (_gate) return _commands.ToList(); } }

    public IReadOnlyList<string> Loads { get { lock (_gate) return _commands.Where(c => c.StartsWith("load ")).Select(c => c[5..]).ToList(); } }

    /// <summary>非 null 时，下一次（以及之后每次）LoadAsync 抛出它。</summary>
    public Exception? LoadException { get; set; }

    public bool Disposed { get; private set; }

    public event Action? FileLoaded;
    public event Action<MpvEndFile>? EndFile;
    public event Action<bool>? PauseChanged;
    public event Action<string>? MediaTitleChanged;
    public event Action<string>? ProcessExited;
    public event Action<bool>? LiveDetected;

    private void Record(string command) { lock (_gate) _commands.Add(command); }

    public Task StartAsync(CancellationToken ct) { Record("start"); return Task.CompletedTask; }

    public Task LoadAsync(string url, CancellationToken ct)
    {
        Record($"load {url}");
        return LoadException is { } e ? Task.FromException(e) : Task.CompletedTask;
    }

    public Task SetPauseAsync(bool paused, CancellationToken ct)
    {
        Record($"pause {paused}");
        PauseChanged?.Invoke(paused);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) { Record("stop"); return Task.CompletedTask; }

    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }

    public void RaiseFileLoaded() => FileLoaded?.Invoke();
    public void RaiseEndFile(MpvEndFile e) => EndFile?.Invoke(e);
    public void RaiseEof() => EndFile?.Invoke(new MpvEndFile(MpvEndReason.Eof, null));
    public void RaiseError(string? text = null) => EndFile?.Invoke(new MpvEndFile(MpvEndReason.Error, text));
    public void RaisePauseChanged(bool paused) => PauseChanged?.Invoke(paused);
    public void RaiseMediaTitle(string title) => MediaTitleChanged?.Invoke(title);
    public void RaiseLiveDetected(bool live) => LiveDetected?.Invoke(live);
    public void RaiseProcessExited(string reason) => ProcessExited?.Invoke(reason);
}
