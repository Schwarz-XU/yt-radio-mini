using YTRadioMini.AppLogic;
using YTRadioMini.Mocks;
using YTRadioMini.Services;
using YTRadioMini.UI;

namespace YTRadioMini;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // 同一时间只允许一个实例（它们共用同一个 state.json）。
        // 如果已经有一个在运行，就让它把窗口提到前台，然后本实例直接退出。
        using var instance = new SingleInstanceGuard(SingleInstanceGuard.DefaultName);
        if (!instance.TryAcquire())
        {
            AllowSetForegroundWindow(-1); // 允许已在运行的实例抢到前台
            instance.SignalExistingInstance();
            return;
        }

        ApplicationConfiguration.Initialize();

        // 异常不能把界面带崩：记录后继续运行（控制器自身会把错误显示在 LCD 上）。
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => System.Diagnostics.Debug.WriteLine($"UI thread exception: {e.Exception}");

        // --demo：使用模拟控制器，不启动 mpv / yt-dlp，用来单独检查界面。
        var radio = args.Contains("--demo", StringComparer.OrdinalIgnoreCase)
            ? new FakeRadioController()
            : CreateController();

        using (var form = new MainForm(radio))
        {
            instance.ActivationRequested += () =>
            {
                try { form.BeginInvoke(form.ActivateFromAnotherInstance); }
                catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException) { /* 窗口正在关闭 */ }
            };

            form.Shown += async (_, _) =>
            {
                try { await radio.RestoreAsync(); }
                catch (Exception e) { System.Diagnostics.Debug.WriteLine($"Restore failed: {e.Message}"); }
            };
            Application.Run(form);
        }

        // 窗口已关闭：保存状态并结束 mpv 与 yt-dlp 子进程。
        try { radio.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(8)); }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine($"Shutdown failed: {e.Message}"); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    private static IRadioController CreateController()
    {
        var mpvPath = FindTool("mpv.exe");
        var ytDlpPath = FindTool("yt-dlp.exe");

        return new RadioController(
            new MpvService(mpvPath),
            new YoutubeResolver(ytDlpPath),
            new StorageService(StorageService.DefaultPath));
    }

    /// <summary>优先使用程序目录（便携包布局）下的文件，找不到则交给系统 PATH 查找。</summary>
    private static string FindTool(string fileName)
    {
        var local = Path.Combine(AppContext.BaseDirectory, fileName);
        return File.Exists(local) ? local : fileName;
    }
}
