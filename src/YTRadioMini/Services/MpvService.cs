namespace YTRadioMini.Services;

// 阶段 0 占位：由 Agent 2 实现。
public sealed class MpvService : IMpvService
{
    public MpvService(string mpvPath) { }

    public event Action? FileLoaded;
    public event Action<MpvEndFile>? EndFile;
    public event Action<bool>? PauseChanged;
    public event Action<string>? MediaTitleChanged;
    public event Action<string>? ProcessExited;

    public Task StartAsync(CancellationToken ct) => throw new NotImplementedException();
    public Task LoadAsync(string url, CancellationToken ct) => throw new NotImplementedException();
    public Task SetPauseAsync(bool paused, CancellationToken ct) => throw new NotImplementedException();
    public Task StopAsync(CancellationToken ct) => throw new NotImplementedException();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
