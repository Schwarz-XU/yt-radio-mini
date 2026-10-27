using YTRadioMini.Models;

namespace YTRadioMini.Services;

/// <summary>
/// state.json 的读写。
/// LoadAsync：文件不存在返回新的空 AppState；文件损坏时先备份再返回空 AppState，不抛异常。
/// SaveAsync：写临时文件后原子替换。
/// </summary>
public interface IStorageService
{
    Task<AppState> LoadAsync(CancellationToken ct);
    Task SaveAsync(AppState state, CancellationToken ct);
}
