namespace YTRadioMini.Models;

public sealed record SavedLink(string Url, DateTimeOffset AddedAt);

/// <summary>state.json 的内容。不保存 Cookie、账号凭据或播放秒数。</summary>
public sealed class AppState
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public List<SavedLink> SavedLinks { get; init; } = new();
    public List<QueueItem> Queue { get; init; } = new();
    public int CurrentIndex { get; init; }
    public bool WasPlaying { get; init; }
}
