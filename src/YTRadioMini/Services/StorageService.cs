using System.Text.Json;
using System.Text.Json.Serialization;
using YTRadioMini.Models;

namespace YTRadioMini.Services;

/// <summary>
/// state.json 的读写。
/// 读取：文件不存在返回空状态；内容损坏、schema 版本更新或体积异常时，
/// 把原文件改名备份后返回空状态；读取时丢弃不是受支持 YouTube 视频的队列条目。
/// 写入：先写同目录临时文件并刷盘，再原子替换目标文件；同一实例内的写入串行执行。
/// </summary>
public sealed class StorageService : IStorageService
{
    public const int MaxSavedLinks = 200;
    public const int MaxTitleLength = 300;
    public const int MaxBackupFiles = 3;
    private const long MaxFileBytes = 64L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string _path;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public StorageService(string stateFilePath)
    {
        _path = stateFilePath;
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YTRadioMini", "state.json");

    public async Task<AppState> LoadAsync(CancellationToken ct)
    {
        byte[] bytes;
        try
        {
            if (!File.Exists(_path)) return new AppState();
            if (new FileInfo(_path).Length > MaxFileBytes)
            {
                BackupCorrupt();
                return new AppState();
            }
            bytes = await File.ReadAllBytesAsync(_path, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // 暂时读不到（被占用、权限问题）：不动原文件，本次按空状态启动。
            return new AppState();
        }

        AppState? loaded;
        try
        {
            loaded = JsonSerializer.Deserialize<AppState>(bytes, JsonOptions);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            loaded = null;
        }

        if (loaded is null || loaded.SchemaVersion > AppState.CurrentSchemaVersion || loaded.SchemaVersion < 1)
        {
            BackupCorrupt();
            return new AppState();
        }

        return Sanitize(loaded);
    }

    public async Task SaveAsync(AppState state, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var tmp = _path + ".tmp";
            await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await fs.WriteAsync(bytes, ct).ConfigureAwait(false);
                await fs.FlushAsync(ct).ConfigureAwait(false);
                fs.Flush(flushToDisk: true);
            }

            if (File.Exists(_path)) File.Replace(tmp, _path, destinationBackupFileName: null);
            else File.Move(tmp, _path);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// 校验并规整刚读到的内容。文件可能被手工修改过，其中的 URL 最终会交给 mpv，
    /// 因此队列条目必须重新通过 LinkParser，并改用规范化 URL。
    /// </summary>
    private static AppState Sanitize(AppState s)
    {
        var queue = new List<QueueItem>();
        var oldToNew = new Dictionary<int, int>();

        var source = s.Queue ?? new List<QueueItem>();
        for (var i = 0; i < source.Count; i++)
        {
            var item = source[i];
            if (item is null || string.IsNullOrWhiteSpace(item.Url)) continue;

            var parsed = LinkParser.Parse(item.Url);
            if (parsed.Valid.Count != 1 || parsed.Valid[0].Kind != LinkKind.Video) continue;

            var link = parsed.Valid[0];
            oldToNew[i] = queue.Count;
            queue.Add(new QueueItem(link.Id, link.Url, CleanTitle(item.Title), item.IsLive));
        }

        var index = 0;
        if (queue.Count > 0)
        {
            // 当前项被丢弃时，尽量落到它之后最近的保留项。
            for (var i = s.CurrentIndex; i < source.Count; i++)
            {
                if (i >= 0 && oldToNew.TryGetValue(i, out var mapped)) { index = mapped; break; }
            }
        }

        var links = new List<SavedLink>();
        foreach (var l in s.SavedLinks ?? new List<SavedLink>())
        {
            if (l is null || string.IsNullOrWhiteSpace(l.Url)) continue;
            if (LinkParser.Parse(l.Url).Valid.Count != 1) continue;
            links.Add(l);
        }
        if (links.Count > MaxSavedLinks) links = links.GetRange(links.Count - MaxSavedLinks, MaxSavedLinks);

        return new AppState
        {
            SchemaVersion = AppState.CurrentSchemaVersion,
            SavedLinks = links,
            Queue = queue,
            CurrentIndex = index,
            WasPlaying = s.WasPlaying,
        };
    }

    private static string CleanTitle(string? title)
    {
        if (string.IsNullOrEmpty(title)) return "";
        var chars = title.Where(c => !char.IsControl(c)).Take(MaxTitleLength).ToArray();
        return new string(chars);
    }

    /// <summary>把无法使用的状态文件改名为 state.json.corrupt-时间戳，并只保留最近几份。</summary>
    private void BackupCorrupt()
    {
        try
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            File.Move(_path, $"{_path}.corrupt-{stamp}", overwrite: true);

            var dir = Path.GetDirectoryName(_path);
            if (string.IsNullOrEmpty(dir)) return;
            var old = new DirectoryInfo(dir)
                .GetFiles(Path.GetFileName(_path) + ".corrupt-*")
                .OrderByDescending(f => f.Name, StringComparer.Ordinal)
                .Skip(MaxBackupFiles);
            foreach (var f in old)
            {
                try { f.Delete(); } catch (IOException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // 备份失败不阻止启动；之后的 SaveAsync 会覆盖原文件。
        }
    }
}
