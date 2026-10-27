namespace YTRadioMini.Services;

// 阶段 0 占位：由 Agent 3 实现。
public sealed class YoutubeResolver : IYoutubeResolver
{
    public YoutubeResolver(string ytDlpPath) { }

    public Task<ResolveResult> ResolveAsync(ParsedLink link, CancellationToken ct) =>
        throw new NotImplementedException();
}
