using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace YTRadioMini.Services;

/// <param name="StartFailed">进程根本没启动（如可执行文件不存在）。</param>
public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut, bool StartFailed);

/// <summary>
/// 运行一个外部程序并收集输出。参数通过 ArgumentList 逐项传递，不经过 shell。
/// 超时或调用方取消时结束整个进程树。
/// </summary>
public static class ProcessRunner
{
    public const int MaxStdErrChars = 16 * 1024;

    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        int maxStdOutChars,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);
        psi.Environment["PYTHONUTF8"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        using var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start()) return new ProcessResult(-1, "", "", false, true);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return new ProcessResult(-1, "", e.Message, false, true);
        }

        ChildProcessJob.TryAssign(process);

        var stdout = ReadCappedAsync(process.StandardOutput, maxStdOutChars);
        var stderr = ReadCappedAsync(process.StandardError, MaxStdErrChars);

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            await DrainAsync(stdout, stderr).ConfigureAwait(false);
            if (ct.IsCancellationRequested) throw;
            timedOut = true;
        }

        var (outText, errText) = await DrainAsync(stdout, stderr).ConfigureAwait(false);
        return new ProcessResult(timedOut ? -1 : process.ExitCode, outText, errText, timedOut, false);
    }

    private static async Task<(string, string)> DrainAsync(Task<string> stdout, Task<string> stderr)
    {
        // 进程结束后管道会关闭；若子孙进程仍持有管道，给读取一个上限，避免永久等待。
        var all = Task.WhenAll(stdout, stderr);
        await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);
        return (stdout.IsCompletedSuccessfully ? stdout.Result : "", stderr.IsCompletedSuccessfully ? stderr.Result : "");
    }

    /// <summary>读完整个流，但只保留前 <paramref name="maxChars"/> 个字符，其余丢弃（仍然读取，避免子进程写管道被阻塞）。</summary>
    private static async Task<string> ReadCappedAsync(StreamReader reader, int maxChars)
    {
        var sb = new StringBuilder();
        var buffer = new char[8192];
        int n;
        while ((n = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
        {
            var room = maxChars - sb.Length;
            if (room > 0) sb.Append(buffer, 0, Math.Min(n, room));
        }
        return sb.ToString();
    }

    public static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // 进程已退出或无权限：忽略。
        }
    }
}
