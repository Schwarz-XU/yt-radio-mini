using YTRadioMini.Models;
using YTRadioMini.Services;

namespace YTRadioMini.AppLogic;

// 阶段 0 占位：由 Agent 5 在集成阶段实现。
public sealed class RadioController : IRadioController
{
    public RadioController(IMpvService mpv, IYoutubeResolver resolver, IStorageService storage) { }

    public RadioViewState State => RadioViewState.Empty;
    public event Action<RadioViewState>? StateChanged;

    public Task RestoreAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task SubmitAsync(string text) => throw new NotImplementedException();
    public Task TogglePauseAsync() => throw new NotImplementedException();
    public Task NextAsync() => throw new NotImplementedException();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
