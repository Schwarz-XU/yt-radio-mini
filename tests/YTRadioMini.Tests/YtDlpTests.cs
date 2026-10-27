using YTRadioMini.Services;

namespace YTRadioMini.Tests;

public class YtDlpPlaylistParserTests
{
    // 结构取自 yt-dlp --flat-playlist --dump-single-json 的典型输出（字段为手写样例，非实测抓取）。
    private const string Sample = """
    {
      "_type": "playlist",
      "id": "PLrAXtmErZgOeiKm4sgNOknGvNjby9efdf",
      "title": "Sample list",
      "entries": [
        { "_type": "url", "ie_key": "Youtube", "id": "AAAAAAAAAAA", "url": "https://www.youtube.com/watch?v=AAAAAAAAAAA", "title": "First 第一首", "live_status": null },
        { "_type": "url", "id": "BBBBBBBBBBB", "title": "[Private video]" },
        { "_type": "url", "id": "CCCCCCCCCCC", "title": "[Deleted video]" },
        { "_type": "url", "id": "DDDDDDDDDDD", "title": "Live now", "live_status": "is_live" },
        { "_type": "url", "id": "UCxxxxxxxxxxxxxxxxxxxxxx", "title": "A channel" },
        { "_type": "url", "title": "no id" },
        { "_type": "url", "id": "EEEEEEEEEEE", "title": "Upcoming", "live_status": "is_upcoming" },
        { "_type": "url", "id": "FFFFFFFFFFF", "title": "Bell\u0007ow\nline" },
        "garbage",
        null
      ]
    }
    """;

    [Fact]
    public void Parse_KeepsOrder_FiltersUnplayable_AndFlagsLive()
    {
        var r = YtDlpPlaylistParser.Parse(Sample);

        Assert.Null(r.Failure);
        Assert.Equal(new[] { "AAAAAAAAAAA", "DDDDDDDDDDD", "EEEEEEEEEEE", "FFFFFFFFFFF" }, r.Items.Select(i => i.Id));
        Assert.Equal("First 第一首", r.Items[0].Title);
        Assert.Equal("https://www.youtube.com/watch?v=AAAAAAAAAAA", r.Items[0].Url);
        Assert.False(r.Items[0].IsLive);
        Assert.True(r.Items[1].IsLive);
        Assert.False(r.Items[2].IsLive);
        Assert.Equal("Bellow line".Replace(" ", ""), r.Items[3].Title.Replace(" ", ""));
    }

    [Fact]
    public void Parse_OnlyPlaceholders_FailsAsUnavailable()
    {
        var r = YtDlpPlaylistParser.Parse("""{"entries":[{"id":"BBBBBBBBBBB","title":"[Private video]"}]}""");
        Assert.Equal(ResolveErrorKind.Unavailable, r.Failure?.Kind);
        Assert.Equal("NO PLAYABLE VIDEOS", r.Failure?.Message);
    }

    [Fact]
    public void Parse_EmptyEntries_FailsAsEmptyPlaylist()
    {
        var r = YtDlpPlaylistParser.Parse("""{"entries":[]}""");
        Assert.Equal("EMPTY PLAYLIST", r.Failure?.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"entries":"x"}""")]
    public void Parse_BadShape_FailsAsParseFailed(string json)
    {
        Assert.Equal(ResolveErrorKind.ParseFailed, YtDlpPlaylistParser.Parse(json).Failure?.Kind);
    }
}

public class YtDlpErrorsTests
{
    [Theory]
    [InlineData("ERROR: [youtube] abc: Private video. Sign in if you've been granted access", ResolveErrorKind.Unavailable)]
    [InlineData("ERROR: [youtube] abc: Video unavailable", ResolveErrorKind.Unavailable)]
    [InlineData("ERROR: [youtube] abc: Video unavailable. This video contains content from X, who has blocked it in your country on copyright grounds", ResolveErrorKind.GeoBlocked)]
    [InlineData("ERROR: [youtube] abc: The uploader has not made this video available in your country", ResolveErrorKind.GeoBlocked)]
    [InlineData("ERROR: [youtube] abc: This live event will begin in 2 hours.", ResolveErrorKind.NotStarted)]
    [InlineData("ERROR: [youtube] abc: Premieres in 3 days", ResolveErrorKind.NotStarted)]
    [InlineData("ERROR: [youtube] abc: Sign in to confirm you're not a bot", ResolveErrorKind.SignInRequired)]
    [InlineData("ERROR: [youtube:tab] Unable to download API page: <urlopen error [Errno 11001] getaddrinfo failed>", ResolveErrorKind.Network)]
    [InlineData("ERROR: Unable to download webpage: HTTP Error 429: Too Many Requests", ResolveErrorKind.Network)]
    [InlineData("[ytdl_hook] youtube-dl executable not found", ResolveErrorKind.ToolMissing)]
    [InlineData("ERROR: The playlist does not exist.", ResolveErrorKind.Unavailable)]
    [InlineData("something completely different", ResolveErrorKind.ParseFailed)]
    [InlineData("", ResolveErrorKind.ParseFailed)]
    [InlineData(null, ResolveErrorKind.ParseFailed)]
    public void Classify_MapsKnownMessages(string? text, ResolveErrorKind expected)
    {
        Assert.Equal(expected, YtDlpErrors.Classify(text));
    }

    [Fact]
    public void ShortMessage_IsDefinedForEveryKind_AndFitsTheLcd()
    {
        foreach (var kind in Enum.GetValues<ResolveErrorKind>())
        {
            var m = YtDlpErrors.ShortMessage(kind);
            Assert.False(string.IsNullOrWhiteSpace(m));
            Assert.True(m.Length <= 20, m);
        }
    }
}

public class YoutubeResolverTests
{
    private static readonly ParsedLink Playlist =
        new(LinkKind.Playlist, "https://www.youtube.com/playlist?list=PLrAXtmErZgOeiKm4sgNOknGvNjby9efdf", "PLrAXtmErZgOeiKm4sgNOknGvNjby9efdf", "x");

    private static YoutubeResolver With(ProcessResult result, Action<IReadOnlyList<string>>? inspect = null) =>
        new("yt-dlp", runner: (_, args, _, _) => { inspect?.Invoke(args); return Task.FromResult(result); });

    [Fact]
    public async Task Video_DoesNotStartYtDlp()
    {
        var resolver = new YoutubeResolver("yt-dlp", runner: (_, _, _, _) => throw new InvalidOperationException("should not run"));
        var r = await resolver.ResolveAsync(new ParsedLink(LinkKind.Video, "https://www.youtube.com/watch?v=AAAAAAAAAAA", "AAAAAAAAAAA", "x"), default);

        var item = Assert.Single(r.Items);
        Assert.Equal("AAAAAAAAAAA", item.Id);
        Assert.Null(r.Failure);
    }

    [Fact]
    public async Task Playlist_PassesUrlAfterDoubleDash_WithoutShell()
    {
        IReadOnlyList<string>? seen = null;
        var r = await With(new ProcessResult(0, """{"entries":[{"id":"AAAAAAAAAAA","title":"a"}]}""", "", false, false), a => seen = a)
            .ResolveAsync(Playlist, default);

        Assert.Null(r.Failure);
        Assert.NotNull(seen);
        Assert.Equal(Playlist.Url, seen![^1]);
        Assert.Equal("--", seen[^2]);
        Assert.Contains("--flat-playlist", seen);
        Assert.Contains("--dump-single-json", seen);
        Assert.Contains("--no-download", seen);
        Assert.Contains("--ignore-config", seen);
    }

    [Fact]
    public async Task Playlist_NonZeroExitWithUsableOutput_IsAccepted()
    {
        var r = await With(new ProcessResult(1, """{"entries":[{"id":"AAAAAAAAAAA","title":"a"}]}""", "ERROR: one entry failed", false, false))
            .ResolveAsync(Playlist, default);
        Assert.Single(r.Items);
    }

    [Fact]
    public async Task Playlist_ErrorOutput_IsClassifiedFromStderr()
    {
        var r = await With(new ProcessResult(1, "", "ERROR: [youtube:tab] The playlist does not exist.", false, false))
            .ResolveAsync(Playlist, default);
        Assert.Equal(ResolveErrorKind.Unavailable, r.Failure?.Kind);
        Assert.Empty(r.Items);
    }

    [Fact]
    public async Task Playlist_StartFailure_IsToolMissing()
    {
        var r = await With(new ProcessResult(-1, "", "not found", false, true)).ResolveAsync(Playlist, default);
        Assert.Equal(ResolveErrorKind.ToolMissing, r.Failure?.Kind);
    }

    [Fact]
    public async Task Playlist_Timeout_IsReported()
    {
        var r = await With(new ProcessResult(-1, "", "", true, false)).ResolveAsync(Playlist, default);
        Assert.Equal(ResolveErrorKind.Timeout, r.Failure?.Kind);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new YoutubeResolver("yt-dlp").ResolveAsync(Playlist, cts.Token));
    }
}

public class ProcessRunnerTests
{
    [Fact]
    public async Task Run_CapturesStdoutAndExitCode()
    {
        var r = await ProcessRunner.RunAsync("dotnet", new[] { "--version" }, TimeSpan.FromSeconds(30), 1024, default);

        Assert.False(r.StartFailed);
        Assert.Equal(0, r.ExitCode);
        Assert.Matches(@"^\d+\.\d+\.\d+", r.StdOut.Trim());
    }

    [Fact]
    public async Task Run_MissingExecutable_ReportsStartFailed()
    {
        var r = await ProcessRunner.RunAsync("definitely-not-a-real-binary-xyz", Array.Empty<string>(), TimeSpan.FromSeconds(5), 1024, default);
        Assert.True(r.StartFailed);
    }

    [Fact]
    public async Task Run_CapsStdout()
    {
        var r = await ProcessRunner.RunAsync("dotnet", new[] { "--info" }, TimeSpan.FromSeconds(30), 20, default);
        Assert.Equal(20, r.StdOut.Length);
    }

    [Fact]
    public async Task Run_ArgumentsAreNotInterpretedByAShell()
    {
        if (OperatingSystem.IsWindows()) return;
        var r = await ProcessRunner.RunAsync("echo", new[] { "$(whoami);", "&&", "x" }, TimeSpan.FromSeconds(5), 1024, default);
        Assert.Equal("$(whoami); && x", r.StdOut.Trim());
    }

    [Fact]
    public async Task Run_TimeoutKillsProcess()
    {
        if (OperatingSystem.IsWindows()) return;
        var started = DateTime.UtcNow;
        var r = await ProcessRunner.RunAsync("sleep", new[] { "30" }, TimeSpan.FromMilliseconds(300), 1024, default);

        Assert.True(r.TimedOut);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Run_CallerCancellationThrowsAndKillsProcess()
    {
        if (OperatingSystem.IsWindows()) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ProcessRunner.RunAsync("sleep", new[] { "30" }, TimeSpan.FromSeconds(60), 1024, cts.Token));
    }
}
