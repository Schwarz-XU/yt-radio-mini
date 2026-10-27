using System.IO.Pipes;

namespace YTRadioMini.Services;

/// <summary>
/// 保证同一 Windows 用户会话里只运行一个程序实例（它们共用同一个 state.json，同时运行会互相覆盖）。
/// 第一个实例持有命名互斥体并监听一个命名管道；之后启动的实例发现互斥体已被占用，
/// 就连一下这个管道，让第一个实例把窗口带到前台，然后自己退出。
/// （激活信号用命名管道而不是命名事件，是因为命名事件只在 Windows 上可用，管道在测试所用的 Linux 上也能工作。）
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly string _name;
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listener;
    private bool _owned;

    /// <param name="name">同一个名字的实例互斥；测试时传入唯一的名字。</param>
    public SingleInstanceGuard(string name)
    {
        _name = name;
        _mutex = new Mutex(false, MutexName(name));
    }

    public static string DefaultName => "YTRadioMini";

    /// <summary>已有另一个实例在运行时触发（在后台线程上），本实例是第一个实例时才会收到。</summary>
    public event Action? ActivationRequested;

    /// <summary>尝试成为第一个实例。成功后开始监听其他实例发来的激活请求。</summary>
    public bool TryAcquire()
    {
        try
        {
            _owned = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            _owned = true; // 上一个实例异常退出，互斥体由我们接管
        }

        if (_owned) StartListening();
        return _owned;
    }

    /// <summary>通知正在运行的第一个实例：把窗口带到前台。找不到第一个实例（刚好退出）时静默返回。</summary>
    public void SignalExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName(_name), PipeDirection.Out);
            client.Connect(1000);
            client.WriteByte(1);
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
        }
    }

    private void StartListening()
    {
        _listener = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        PipeName(_name), PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
                    _ = await server.ReadAsync(new byte[1], _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException)
                {
                    continue; // 客户端中途断开：继续等下一个
                }

                try { ActivationRequested?.Invoke(); }
                catch (Exception) { /* 订阅者的异常不能终止监听 */ }
            }
        });
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener?.Wait(TimeSpan.FromSeconds(1)); }
        catch (AggregateException) { }

        if (_owned)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { /* 不是持有线程：进程退出时由系统释放 */ }
        }
        _mutex.Dispose();
        _cts.Dispose();
    }

    // "Local\" 前缀把范围限定在当前登录会话，不同用户可以各自运行一个实例。
    private static string MutexName(string name) => OperatingSystem.IsWindows() ? $@"Local\{name}.Instance" : $"{name}.Instance";
    private static string PipeName(string name) => $"{name}.Activate.{Environment.UserName}";
}
