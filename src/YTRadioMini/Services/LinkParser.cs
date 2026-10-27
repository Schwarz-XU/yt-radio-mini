namespace YTRadioMini.Services;

/// <summary>
/// 把用户输入的文本拆成行并校验为受支持的 YouTube 链接。
/// 使用结构化的 <see cref="Uri"/> 解析主机、协议与路径，不对整行做字符串包含判断。
/// 输出的 URL 都是规范化后的 https 链接，不保留原始查询参数（如 t=、si=）。
/// </summary>
public static class LinkParser
{
    public const int MaxLineLength = 2048;

    // 允许的主机（Uri.Host 已是小写）。
    private static readonly HashSet<string> YoutubeHosts = new(StringComparer.Ordinal)
    {
        "youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com",
    };

    private const string ShortHost = "youtu.be";

    public static LinkParseResult Parse(string? input)
    {
        var valid = new List<ParsedLink>();
        var invalid = new List<InvalidLink>();

        if (string.IsNullOrWhiteSpace(input))
            return new LinkParseResult(valid, invalid);

        foreach (var raw in input.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var outcome = ParseLine(line, out var link, out var reason);
            if (outcome) valid.Add(link!);
            else invalid.Add(new InvalidLink(Truncate(line), reason!));
        }

        return new LinkParseResult(valid, invalid);
    }

    private static bool ParseLine(string line, out ParsedLink? link, out string? reason)
    {
        link = null;
        reason = null;

        if (line.Length > MaxLineLength) { reason = "TOO LONG"; return false; }

        if (!Uri.TryCreate(line, UriKind.Absolute, out var uri))
        {
            reason = "NOT A URL";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            reason = "UNSUPPORTED LINK";
            return false;
        }

        // 带用户信息或非默认端口的链接一律拒绝，避免伪装主机。
        if (!string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort)
        {
            reason = "UNSUPPORTED LINK";
            return false;
        }

        var path = uri.AbsolutePath;
        var query = ParseQuery(uri.Query);

        string? videoId = null;
        string? listId = null;

        if (uri.Host == ShortHost)
        {
            // https://youtu.be/VIDEO_ID
            var segments = Segments(path);
            if (segments.Length != 1) { reason = "UNSUPPORTED LINK"; return false; }
            videoId = segments[0];
            query.TryGetValue("list", out listId);
        }
        else if (YoutubeHosts.Contains(uri.Host))
        {
            var segments = Segments(path);

            if (segments.Length == 1 && segments[0] == "watch")
            {
                query.TryGetValue("v", out videoId);
                query.TryGetValue("list", out listId);
            }
            else if (segments.Length == 2 && segments[0] is "live" or "shorts")
            {
                videoId = segments[1];
                query.TryGetValue("list", out listId);
            }
            else if (segments.Length == 1 && segments[0] == "playlist")
            {
                if (!query.TryGetValue("list", out listId)) { reason = "BAD LIST ID"; return false; }
                if (!IsValidListId(listId)) { reason = "BAD LIST ID"; return false; }

                // "RD" + 视频 ID 是以该视频为种子的自动合辑，要用带 v= 的 watch 地址展开。
                if (MixSeedVideoId(listId) is { } seed)
                {
                    link = new ParsedLink(LinkKind.Playlist, MixUrl(seed, listId), listId, line, seed);
                    return true;
                }

                link = new ParsedLink(LinkKind.Playlist, PlaylistUrl(listId), listId, line);
                return true;
            }
            else
            {
                reason = "UNSUPPORTED LINK";
                return false;
            }
        }
        else
        {
            reason = "UNSUPPORTED LINK";
            return false;
        }

        if (videoId is null || !IsValidVideoId(videoId)) { reason = "BAD VIDEO ID"; return false; }

        // 同时带有效 list 参数的链接优先按 Playlist 处理；list 无效则退回单个视频。
        if (listId is not null && IsValidListId(listId))
        {
            link = IsMixList(listId)
                ? new ParsedLink(LinkKind.Playlist, MixUrl(videoId, listId), listId, line, videoId)
                : new ParsedLink(LinkKind.Playlist, PlaylistUrl(listId), listId, line);
            return true;
        }

        link = new ParsedLink(LinkKind.Video, VideoUrl(videoId), videoId, line);
        return true;
    }

    /// <summary>
    /// YouTube 自动生成的合辑（Mix / Radio）：列表 ID 以 "RD" 开头。
    /// "RDCLAK" 开头的是 YouTube Music 的正式专辑/歌单，可以正常展开，不算合辑。
    /// </summary>
    public static bool IsMixList(string listId) =>
        listId.StartsWith("RD", StringComparison.Ordinal) && !listId.StartsWith("RDCLAK", StringComparison.Ordinal);

    /// <summary>"RD" + 11 位视频 ID 或 "RDAMVM" + 11 位视频 ID 形式的合辑返回种子视频 ID，否则返回 null。</summary>
    private static string? MixSeedVideoId(string listId)
    {
        foreach (var prefix in new[] { "RDAMVM", "RD" })
        {
            if (listId.StartsWith(prefix, StringComparison.Ordinal) && listId.Length == prefix.Length + 11)
            {
                var id = listId[prefix.Length..];
                if (IsValidVideoId(id)) return id;
            }
        }
        return null;
    }

    public static string MixUrl(string seedVideoId, string listId) =>
        $"https://www.youtube.com/watch?v={seedVideoId}&list={listId}";

    public static string VideoUrl(string id) => $"https://www.youtube.com/watch?v={id}";
    public static string PlaylistUrl(string id) => $"https://www.youtube.com/playlist?list={id}";

    public static bool IsValidVideoId(string id) =>
        id.Length == 11 && id.All(IsIdChar);

    public static bool IsValidListId(string id) =>
        id.Length is >= 10 and <= 64 && id.All(IsIdChar);

    private static bool IsIdChar(char c) =>
        c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-';

    private static string[] Segments(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>解析查询字符串；同名参数取第一个。</summary>
    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(query)) return result;

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = eq < 0 ? pair : pair[..eq];
            var value = eq < 0 ? "" : pair[(eq + 1)..];
            try
            {
                key = Uri.UnescapeDataString(key);
                value = Uri.UnescapeDataString(value);
            }
            catch (UriFormatException)
            {
                continue;
            }
            result.TryAdd(key, value);
        }
        return result;
    }

    private static string Truncate(string s) => s.Length <= 120 ? s : s[..120] + "…";
}
