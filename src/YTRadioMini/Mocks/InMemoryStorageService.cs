using YTRadioMini.Models;
using YTRadioMini.Services;

namespace YTRadioMini.Mocks;

public sealed class InMemoryStorageService : IStorageService
{
    private AppState _state = new();

    public Task<AppState> LoadAsync(CancellationToken ct) => Task.FromResult(_state);

    public Task SaveAsync(AppState state, CancellationToken ct)
    {
        _state = state;
        return Task.CompletedTask;
    }
}
