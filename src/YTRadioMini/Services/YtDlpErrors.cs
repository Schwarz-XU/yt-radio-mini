namespace YTRadioMini.Services;

/// <summary>把 yt-dlp / mpv 的错误文本归类，并给出适合 LCD 显示的简短提示。</summary>
public static class YtDlpErrors
{
    // 判断顺序有意义：先检查更具体的原因（例如地区限制的消息里同时含 "Video unavailable"）。
    private static readonly (ResolveErrorKind Kind, string[] Needles)[] Rules =
    {
        (ResolveErrorKind.GeoBlocked, new[]
        {
            "in your country", "not available in your", "geo restriction", "geo-restrict", "geo restricted",
        }),
        (ResolveErrorKind.NotStarted, new[]
        {
            "live event will begin", "premieres in", "will begin in", "this live event has not started",
        }),
        (ResolveErrorKind.SignInRequired, new[]
        {
            "not a bot", "confirm you’re not a bot", "sign in to confirm",
        }),
        (ResolveErrorKind.ToolMissing, new[]
        {
            "executable not found", "youtube-dl not found", "yt-dlp not found",
            "is not recognized as an internal or external command",
        }),
        (ResolveErrorKind.Unavailable, new[]
        {
            "private video", "video unavailable", "this video is unavailable", "this video is not available",
            "has been removed", "has been terminated", "account associated with this video",
            "members-only", "members only", "join this channel", "age-restricted",
            "playlist does not exist", "does not exist", "is private", "no video formats found",
            "requested format is not available",
        }),
        (ResolveErrorKind.Network, new[]
        {
            "getaddrinfo failed", "temporary failure in name resolution", "name or service not known",
            "no address associated", "network is unreachable", "connection reset", "connection refused",
            "connection aborted", "timed out", "unable to download", "urlopen error", "failed to resolve",
            "failed to establish a new connection", "http error 5", "http error 429",
        }),
    };

    /// <summary>无法归类时返回 <paramref name="fallback"/>。</summary>
    public static ResolveErrorKind Classify(string? text, ResolveErrorKind fallback = ResolveErrorKind.ParseFailed)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;

        foreach (var (kind, needles) in Rules)
            foreach (var needle in needles)
                if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    return kind;

        return fallback;
    }

    /// <summary>LCD 上显示的简短文字（大写、不超过约 20 个字符）。</summary>
    public static string ShortMessage(ResolveErrorKind kind) => kind switch
    {
        ResolveErrorKind.Network => "NO NETWORK",
        ResolveErrorKind.Unavailable => "VIDEO UNAVAILABLE",
        ResolveErrorKind.GeoBlocked => "REGION BLOCKED",
        ResolveErrorKind.NotStarted => "LIVE NOT STARTED",
        ResolveErrorKind.Timeout => "TIMED OUT",
        ResolveErrorKind.ToolMissing => "YT-DLP MISSING",
        ResolveErrorKind.SignInRequired => "SIGN-IN REQUIRED",
        _ => "CAN'T PLAY",
    };
}
