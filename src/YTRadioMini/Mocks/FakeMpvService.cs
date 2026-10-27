using YTRadioMini.Services;

namespace YTRadioMini.Mocks;

/// <summary>记录收到的命令，并允许测试代码手动触发 mpv 事件。</summary>
public sealed class FakeMpvService : IMpvService
{
    public List<string> Commands { get; } = new();

    public event Action? FileLoaded;
    public event Action<MpvEndFile>? EndFile;
    public event Action<bool>? PauseChanged;
    public event Action<string>? MediaTitleChanged;
    public event Action<string>? ProcessExited;

    public Task StartAsync(CancellationToken ct) { Commands.Add("start"); return Task.CompletedTask; }
    public Task LoadAsync(string url, CancellationToken ct) { Commands.Add($"load {url}"); return Task.CompletedTask; }
    public Task SetPauseAsync(bool paused, CancellationToken ct) { Commands.Add($"pause {paused}"); return Task.CompletedTask; }
    public Task StopAsync(CancellationToken ct) { Commands.Add("stop"); return Task.CompletedTask; }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void RaiseFileLoaded() => FileLoaded?.Invoke();
    public void RaiseEndFile(MpvEndFile e) => EndFile?.Invoke(e);
    public void RaisePauseChanged(bool paused) => PauseChanged?.Invoke(paused);
    public void RaiseMediaTitle(string title) => MediaTitleChanged?.Invoke(title);
    public void RaiseProcessExited(string reason) => ProcessExited?.Invoke(reason);
}
