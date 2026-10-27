using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;

namespace YTRadioMini.Services;

public sealed class MpvStartException : Exception
{
    public MpvStartException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class MpvServiceOptions
{
    /// <summary>附加在默认参数之后的 mpv 参数（测试时用来指定 --ao=null 等）。</summary>
    public IReadOnlyList<string> ExtraArguments { get; init; } = Array.Empty<string>();

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>判断是否为直播时，两次读取时长之间的间隔与读取次数。</summary>
    public TimeSpan LiveProbeInterval { get; init; } = TimeSpan.FromSeconds(2);
    public int LiveProbeCount { get; init; } = 4;
}

/// <summary>
/// 管理一个外部 mpv 进程，并通过 JSON IPC（Windows 命名管道）控制它。
/// 进程在第一次需要时启动；意外退出后触发 <see cref="ProcessExited"/>，下一次 StartAsync/LoadAsync 会重新启动。
/// 本程序启动的 mpv 会被放进 Job Object，本程序退出时由系统一并回收。
/// </summary>
public sealed class MpvService : IMpvService
{
    private const int ObservePauseId = 1;
    private const int ObserveTitleId = 2;

    private readonly string _mpvPath;
    private readonly MpvServiceOptions _options;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private Instance? _current;
    private bool _disposed;

    public MpvService(string mpvPath, MpvServiceOptions? options = null)
    {
        _mpvPath = mpvPath;
        _options = options ?? new MpvServiceOptions();
    }

    public event Action? FileLoaded;
    public event Action<MpvEndFile>? EndFile;
    public event Action<bool>? PauseChanged;
    public event Action<string>? MediaTitleChanged;
    public event Action<string>? ProcessExited;
    public event Action<bool>? LiveDetected;

    public async Task StartAsync(CancellationToken ct)
    {
        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureStartedLockedAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task LoadAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("Only http(s) URLs can be loaded.", nameof(url));
        }

        var inst = await GetInstanceAsync(ct).ConfigureAwait(false);
        inst.LastError = null;
        inst.NextSerial(); // 作废上一个文件尚未完成的直播判断

        // 先取消暂停，保证在暂停状态下切歌也会直接播放。
        await inst.Client.SendAsync(new object?[] { "set_property", "pause", false }, _options.CommandTimeout, ct).ConfigureAwait(false);
        await inst.Client.SendAsync(new object?[] { "loadfile", url, "replace" }, _options.CommandTimeout, ct).ConfigureAwait(false);
    }

    public async Task SetPauseAsync(bool paused, CancellationToken ct)
    {
        var inst = await GetInstanceAsync(ct).ConfigureAwait(false);
        await inst.Client.SendAsync(new object?[] { "set_property", "pause", paused }, _options.CommandTimeout, ct).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        var inst = _current;
        if (inst is null || inst.Dead) return;
        inst.NextSerial();
        await inst.Client.SendAsync(new object?[] { "stop" }, _options.CommandTimeout, ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            var inst = _current;
            _current = null;
            if (inst is not null) await ShutdownAsync(inst).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task<Instance> GetInstanceAsync(CancellationToken ct)
    {
        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await EnsureStartedLockedAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task<Instance> EnsureStartedLockedAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_current is { Dead: false } alive) return alive;
        if (_current is not null)
        {
            await ShutdownAsync(_current).ConfigureAwait(false);
            _current = null;
        }

        _current = await LaunchAsync(ct).ConfigureAwait(false);
        return _current;
    }

    private async Task<Instance> LaunchAsync(CancellationToken ct)
    {
        var pipeName = $"ytradiomini-{Environment.ProcessId}-{Guid.NewGuid():N}";
        var pipePath = OperatingSystem.IsWindows()
            ? $@"\\.\pipe\{pipeName}"
            : Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + pipeName); // .NET 在类 Unix 系统上命名管道所用的 socket 路径

        var psi = new ProcessStartInfo(_mpvPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in BuildArguments(pipePath)) psi.ArgumentList.Add(arg);
        foreach (var arg in _options.ExtraArguments) psi.ArgumentList.Add(arg);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        try
        {
            if (!process.Start()) throw new MpvStartException("mpv did not start");
        }
        catch (Exception e) when (e is Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            process.Dispose();
            throw new MpvStartException("mpv could not be started", e);
        }

        ChildProcessJob.TryAssign(process);

        var inst = new Instance(process);
        process.Exited += (_, _) => OnDead(inst, $"mpv exited ({SafeExitCode(process)})");

        try
        {
            var stream = await ConnectAsync(pipeName, process, ct).ConfigureAwait(false);
            inst.Client = new MpvIpcClient(stream);
            inst.Client.EventReceived += e => OnEvent(inst, e);
            inst.Client.Disconnected += _ => OnDead(inst, "mpv IPC lost");
            inst.Client.Start();

            var t = _options.CommandTimeout;
            await inst.Client.SendAsync(new object?[] { "observe_property", ObservePauseId, "pause" }, t, ct).ConfigureAwait(false);
            await inst.Client.SendAsync(new object?[] { "observe_property", ObserveTitleId, "media-title" }, t, ct).ConfigureAwait(false);
            await inst.Client.SendAsync(new object?[] { "request_log_messages", "error" }, t, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await ShutdownAsync(inst).ConfigureAwait(false);
            throw e as MpvStartException ?? new MpvStartException("mpv IPC could not be established", e);
        }
        catch (OperationCanceledException)
        {
            await ShutdownAsync(inst).ConfigureAwait(false);
            throw;
        }

        return inst;
    }

    private static IEnumerable<string> BuildArguments(string pipePath) => new[]
    {
        "--no-config",
        "--idle=yes",
        "--force-window=no",
        "--vid=no",
        "--audio-display=no",
        "--ytdl=yes",
        "--ytdl-format=bestaudio/best",
        "--ytdl-raw-options=ignore-config=", // 不让用户全局的 yt-dlp 配置影响解析
        "--terminal=no",
        // 只缓冲音频，限制内存：默认的 150 MiB 缓冲上限对电台用途没有必要。
        "--demuxer-max-bytes=16MiB",
        "--demuxer-max-back-bytes=4MiB",
        "--input-ipc-server=" + pipePath,
    };

    private async Task<Stream> ConnectAsync(string pipeName, Process process, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + _options.ConnectTimeout;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (process.HasExited) throw new MpvStartException($"mpv exited during startup ({SafeExitCode(process)})");
            if (DateTime.UtcNow >= deadline) throw new MpvStartException("mpv IPC connection timed out");

            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(250, ct).ConfigureAwait(false);
                return pipe;
            }
            catch (Exception e) when (e is TimeoutException or IOException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
            catch
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    private void OnEvent(Instance inst, MpvEvent e)
    {
        if (inst.Dead) return;
        var root = e.Root;

        switch (e.Name)
        {
            case "file-loaded":
                var serial = inst.NextSerial();
                Raise(() => FileLoaded?.Invoke());
                _ = DetectLiveAsync(inst, serial);
                break;

            case "end-file":
                inst.NextSerial();
                var reason = ParseReason(root);
                string? text = null;
                if (reason == MpvEndReason.Error)
                {
                    text = inst.LastError;
                    if (text is null && root.TryGetProperty("file_error", out var fe) && fe.ValueKind == JsonValueKind.String)
                        text = fe.GetString();
                }
                Raise(() => EndFile?.Invoke(new MpvEndFile(reason, text)));
                break;

            case "property-change":
                if (!root.TryGetProperty("name", out var nameEl)) break;
                var data = root.TryGetProperty("data", out var d) ? d : default;
                if (nameEl.GetString() == "pause" && data.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    var paused = data.ValueKind == JsonValueKind.True;
                    Raise(() => PauseChanged?.Invoke(paused));
                }
                else if (nameEl.GetString() == "media-title" && data.ValueKind == JsonValueKind.String)
                {
                    var title = data.GetString();
                    if (!string.IsNullOrWhiteSpace(title)) Raise(() => MediaTitleChanged?.Invoke(title));
                }
                break;

            case "log-message":
                if (root.TryGetProperty("text", out var txt) && txt.ValueKind == JsonValueKind.String)
                {
                    var line = txt.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(line)) inst.LastError = line.Length > 500 ? line[..500] : line;
                }
                break;
        }
    }

    private static MpvEndReason ParseReason(JsonElement root) =>
        root.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String
            ? r.GetString() switch
            {
                "eof" => MpvEndReason.Eof,
                "stop" => MpvEndReason.Stop,
                "quit" => MpvEndReason.Quit,
                "error" => MpvEndReason.Error,
                "redirect" => MpvEndReason.Redirect,
                _ => MpvEndReason.Unknown,
            }
            : MpvEndReason.Unknown;

    /// <summary>
    /// 判断当前文件是否为直播。mpv 对直播流（如没有 ENDLIST 的 HLS）会报告一个随缓冲不断变长的 duration，
    /// 点播文件的 duration 则保持不变，所以隔一段时间读取几次：
    /// 读不到时长，或者时长比第一次增加超过 1 秒，就认定为直播；每次读取的结果都稳定则认定为点播。
    /// 这是启发式判断，只对仍是当前文件的结果触发事件。
    /// </summary>
    private async Task DetectLiveAsync(Instance inst, long serial)
    {
        try
        {
            double? first = null;
            for (var i = 0; i < _options.LiveProbeCount; i++)
            {
                if (i > 0) await Task.Delay(_options.LiveProbeInterval).ConfigureAwait(false);
                if (inst.Dead || inst.CurrentSerial != serial) return;

                var duration = await GetDurationAsync(inst).ConfigureAwait(false);
                if (inst.Dead || inst.CurrentSerial != serial) return;

                if (duration is null || (first is { } f && duration.Value - f > 1.0))
                {
                    Raise(() => LiveDetected?.Invoke(true));
                    return;
                }
                first ??= duration;
            }
            Raise(() => LiveDetected?.Invoke(false));
        }
        catch (Exception)
        {
            // 判断失败时保持原状：不触发事件。
        }
    }

    private async Task<double?> GetDurationAsync(Instance inst)
    {
        try
        {
            var data = await inst.Client.SendAsync(new object?[] { "get_property", "duration" }, _options.CommandTimeout, CancellationToken.None).ConfigureAwait(false);
            return data.ValueKind == JsonValueKind.Number ? data.GetDouble() : null;
        }
        catch (MpvCommandException)
        {
            return null; // property unavailable
        }
    }

    private void OnDead(Instance inst, string reason)
    {
        if (Interlocked.Exchange(ref inst.DeadFlag, 1) != 0) return;
        if (_disposed || inst.ShuttingDown) return;
        Raise(() => ProcessExited?.Invoke(reason));
    }

    private static async Task ShutdownAsync(Instance inst)
    {
        inst.ShuttingDown = true;
        Interlocked.Exchange(ref inst.DeadFlag, 1);

        try
        {
            if (inst.ClientOrNull is { IsConnected: true } c)
                await c.SendAsync(new object?[] { "quit" }, TimeSpan.FromMilliseconds(500), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception) { }

        try
        {
            if (!inst.Process.HasExited)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try { await inst.Process.WaitForExitAsync(cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }
        catch (InvalidOperationException) { }

        ProcessRunner.KillTree(inst.Process); // 同时回收 mpv 启动的 yt-dlp

        if (inst.ClientOrNull is { } client) await client.DisposeAsync().ConfigureAwait(false);
        inst.Process.Dispose();
    }

    private static int SafeExitCode(Process p)
    {
        try { return p.ExitCode; }
        catch (InvalidOperationException) { return -1; }
    }

    private static void Raise(Action a)
    {
        try { a(); }
        catch (Exception) { /* 订阅者的异常不能影响 IPC 读取线程 */ }
    }

    private sealed class Instance
    {
        public Instance(Process process) => Process = process;

        public Process Process { get; }
        public MpvIpcClient? ClientOrNull { get; private set; }
        public MpvIpcClient Client
        {
            get => ClientOrNull ?? throw new InvalidOperationException("IPC not connected");
            set => ClientOrNull = value;
        }

        public int DeadFlag;
        public bool Dead => Volatile.Read(ref DeadFlag) != 0;
        public volatile bool ShuttingDown;
        public volatile string? LastError;
        private long _fileSerial;
        public long NextSerial() => Interlocked.Increment(ref _fileSerial);
        public long CurrentSerial => Interlocked.Read(ref _fileSerial);
    }
}
