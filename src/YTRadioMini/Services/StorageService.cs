using YTRadioMini.Models;

namespace YTRadioMini.Services;

// 阶段 0 占位：由 Agent 4 实现。
public sealed class StorageService : IStorageService
{
    public StorageService(string stateFilePath) { }

    public Task<AppState> LoadAsync(CancellationToken ct) => throw new NotImplementedException();
    public Task SaveAsync(AppState state, CancellationToken ct) => throw new NotImplementedException();
}
