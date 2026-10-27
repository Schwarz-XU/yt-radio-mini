using YTRadioMini.Models;
using YTRadioMini.Services;

namespace YTRadioMini.Mocks;

/// <summary>确定性的模拟解析器。Playlist 展开为 3 条；ID 含 "bad" 时返回 Unavailable。</summary>
public sealed class FakeYoutubeResolver : IYoutubeResolver
{
    public Task<ResolveResult> ResolveAsync(ParsedLink link, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (link.Id.Contains("bad", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(ResolveResult.Fail(ResolveErrorKind.Unavailable, "VIDEO UNAVAILABLE"));

        if (link.Kind == LinkKind.Playlist)
        {
            var items = Enumerable.Range(1, 3)
                .Select(i => new QueueItem($"{link.Id}-{i}", $"https://www.youtube.com/watch?v={link.Id}-{i}", $"Playlist track {i}", false))
                .ToList();
            return Task.FromResult(ResolveResult.Ok(items));
        }

        var one = new QueueItem(link.Id, link.Url, "", link.Id.Contains("live", StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(ResolveResult.Ok(new[] { one }));
    }
}
