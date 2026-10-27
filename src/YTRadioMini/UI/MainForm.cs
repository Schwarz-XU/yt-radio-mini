using System.Runtime.InteropServices;
using YTRadioMini.AppLogic;
using YTRadioMini.Models;

namespace YTRadioMini.UI;

/// <summary>
/// 主窗口：只做显示和转发输入。播放状态全部来自 <see cref="IRadioController"/>，
/// 窗口本身不保存任何播放状态，也不直接接触 mpv 或 yt-dlp。
/// </summary>
public sealed class MainForm : Form
{
    private const string Hint = "Paste link. Enter=add, Ctrl+Enter=newline";

    private readonly IRadioController _radio;
    private readonly RadioSurface _surface = new() { Dock = DockStyle.Fill };
    private readonly LinkTextBox _input = new();
    private Font? _inputFont;
    private bool _hintShown;

    public MainForm(IRadioController radio)
    {
        _radio = radio;

        // 没有系统标题栏：窗口就是收音机本身，最小化与关闭按钮画在机身右上角。
        // Text 不会显示在窗口里，只用于任务栏悬停提示、Alt+Tab 和屏幕阅读器。
        Text = "YT Radio Mini";
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = LoadIcon();
        AutoScaleMode = AutoScaleMode.None; // 缩放由 ApplyScale 和 RadioSurface 的自绘负责
        BackColor = RetroTheme.Shell;
        KeyPreview = true;
        DoubleBuffered = true;

        _input.Multiline = true;      // 允许粘贴多行；Enter 由代码接管，不会插入换行
        _input.WordWrap = false;
        _input.ScrollBars = ScrollBars.None;
        _input.AcceptsReturn = false;
        _input.AcceptsTab = false;
        _input.TabStop = false;       // 启动时不抢焦点，Space / N 立即可用
        _input.MaxLength = 20000;
        _input.BorderStyle = BorderStyle.FixedSingle;
        _input.BackColor = RetroTheme.InputBackground;
        _input.ForeColor = RetroTheme.InputText;
        _input.AccessibleName = "YouTube link";

        Controls.Add(_input);
        Controls.Add(_surface);
        _input.BringToFront();

        _surface.MinimizeClicked += () => WindowState = FormWindowState.Minimized;
        _surface.CloseClicked += Close;
        _surface.DragRequested += BeginWindowDrag;
        _surface.PreviousClicked += () => Fire(_radio.PreviousAsync);
        _surface.PlayPauseClicked += () => Fire(_radio.TogglePauseAsync);
        _surface.NextClicked += () => Fire(_radio.NextAsync);
        _surface.PlayIndexRequested += i => Fire(() => _radio.PlayIndexAsync(i));
        _surface.RemoveRequested += i => Fire(() => _radio.RemoveAsync(i));
        _surface.MoveRequested += (from, to) => Fire(() => _radio.MoveAsync(from, to));
        _surface.ClearRequested += () => Fire(_radio.ClearAsync);
        _surface.SizeChanged += (_, _) => LayoutInput();
        _input.KeyDown += OnInputKeyDown;
        _input.GotFocus += (_, _) => HideHint();
        _input.Leave += (_, _) => ShowHintIfEmpty();
        _input.TextChanged += (_, _) => UpdateInputNote();

        ApplyScale(DeviceDpi);
        ShowHintIfEmpty();

        _radio.StateChanged += OnStateChanged;
        _surface.View = _radio.State;
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyScale(e.DeviceDpiNew);
    }

    /// <summary>客户区固定为 360 × 240 的 DPI 缩放值（100% DPI 为基准）。</summary>
    private void ApplyScale(int dpi)
    {
        var s = dpi / 96f;
        ClientSize = new Size((int)Math.Round(RadioSurface.BaseWidth * s), (int)Math.Round(RadioSurface.BaseHeight * s));
        LayoutInput();
        ApplyRoundedShape();
    }

    // ───────────── 无边框窗口 ─────────────

    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_SYSMENU = 0x00080000;
    private const int CS_DROPSHADOW = 0x00020000;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // 无边框窗口默认不能通过点击任务栏图标来最小化/还原，也没有系统菜单；补上这两个样式。
            cp.Style |= WS_MINIMIZEBOX | WS_SYSMENU;
            cp.ClassStyle |= CS_DROPSHADOW; // 轻微的窗口阴影
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            // Windows 11：让系统把窗口角画成圆角（边缘平滑）。
            var preference = DWMWCP_ROUND;
            DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }
        catch (Exception e2) when (e2 is DllNotFoundException or EntryPointNotFoundException)
        {
            // 较旧的系统没有这个接口：下面用 Region 兜底。
        }
        ApplyRoundedShape();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyRoundedShape();
    }

    /// <summary>Windows 11 之前的系统不会自动画圆角，用窗口区域裁出圆角（边缘没有抗锯齿，但只有四个小角）。</summary>
    private void ApplyRoundedShape()
    {
        if (!IsHandleCreated || WindowState == FormWindowState.Minimized) return;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Region = null;
            return;
        }

        var radius = RadioSurface.WindowRadius * (Width / RadioSurface.BaseWidth);
        using var path = RadioSurface.RoundedRect(new RectangleF(0, 0, Width, Height), radius);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    /// <summary>在机身空白处按住鼠标即可拖动窗口（把这次按下交给系统当作点击标题栏）。</summary>
    private void BeginWindowDrag()
    {
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
    }

    /// <summary>用户又启动了一个程序副本：把已有的窗口带到前台。</summary>
    public void ActivateFromAnotherInstance()
    {
        if (IsDisposed) return;
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Show();
        // Windows 限制后台程序抢前台；先短暂置顶再取消，可以可靠地把窗口提到最前。
        TopMost = true;
        TopMost = false;
        Activate();
    }

    private static Icon? LoadIcon()
    {
        try
        {
            using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("app.ico");
            return stream is null ? null : new Icon(stream);
        }
        catch (Exception e) when (e is ArgumentException or IOException)
        {
            return null; // 图标缺失不影响使用
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    private void LayoutInput()
    {
        var s = _surface.Width / RadioSurface.BaseWidth;
        _input.Bounds = _surface.ToClient(RadioSurface.InputRect);

        var old = _inputFont;
        _inputFont = new Font(RetroTheme.LcdFontFamily, 13f * s, FontStyle.Regular, GraphicsUnit.Pixel);
        _input.Font = _inputFont;
        old?.Dispose();
    }

    // ───────────── 状态显示 ─────────────

    private void OnStateChanged(RadioViewState state)
    {
        if (IsDisposed) return;
        if (!InvokeRequired)
        {
            _surface.View = state;
            return;
        }

        try { BeginInvoke(() => { if (!IsDisposed) _surface.View = state; }); }
        catch (InvalidOperationException) { /* 窗口正在关闭 */ }
    }

    // ───────────── 键盘 ─────────────

    /// <summary>
    /// 窗口级快捷键。放在 ProcessCmdKey 而不是 KeyDown 里，是因为方向键等会先被窗口的焦点导航消耗掉。
    /// 输入框有焦点时，这些键都属于输入框。
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_input.Focused) return base.ProcessCmdKey(ref msg, keyData);

        var key = keyData & Keys.KeyCode;
        var modifiers = keyData & Keys.Modifiers;

        if (_surface.HandleKey(key, modifiers)) return true;

        switch (key)
        {
            case Keys.Space when modifiers == Keys.None:
                Fire(_radio.TogglePauseAsync);
                return true;

            case Keys.N when modifiers == Keys.None:
                Fire(_radio.NextAsync);
                return true;

            case Keys.P when modifiers == Keys.None:
                Fire(_radio.PreviousAsync);
                return true;

            case Keys.V when modifiers == Keys.Control:
                // 没有焦点时 Ctrl+V 直接把剪贴板内容送进输入框。
                _input.Focus();
                _input.Paste();
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = e.Handled = true;
            if (e.Control)
            {
                // Ctrl+Enter：另起一行，用来逐条输入多个链接。
                _input.SelectedText = "\r\n";
            }
            else
            {
                // Enter（含 Shift+Enter）：提交输入框里的全部内容（一行或多行）。
                Submit();
            }
        }
        else if (e.KeyCode == Keys.Escape)
        {
            e.SuppressKeyPress = e.Handled = true;
            ActiveControl = null; // 放弃输入，把焦点还给窗口，Space / N 恢复可用
        }
    }

    /// <summary>输入框一次只能看到一行，多行时在上方提示共有几条。</summary>
    private void UpdateInputNote()
    {
        var n = _hintShown ? 0 : _input.LineCountNonEmpty;
        _surface.InputNote = n > 1 ? $"{n} LINKS READY - ENTER TO ADD" : "";
    }

    private void Submit()
    {
        var text = _hintShown ? "" : _input.Text;
        if (string.IsNullOrWhiteSpace(text)) return;

        _input.Clear();
        ActiveControl = null;
        ShowHintIfEmpty();
        Fire(() => _radio.SubmitAsync(text));
    }

    // ───────────── 输入框提示文字 ─────────────

    private void HideHint()
    {
        if (!_hintShown) return;
        _hintShown = false;
        _input.Clear();
        _input.ForeColor = RetroTheme.InputText;
    }

    private void ShowHintIfEmpty()
    {
        if (_input.Focused || _input.TextLength > 0) return;
        _hintShown = true;
        _input.ForeColor = RetroTheme.InputHint;
        _input.Text = Hint;
    }

    /// <summary>控制器的方法不会向 UI 抛异常，这里再兜一层，保证事件处理里不出现未捕获异常。</summary>
    private static async void Fire(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine($"UI command failed: {e.Message}"); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _radio.StateChanged -= OnStateChanged;
            _inputFont?.Dispose();
        }
        base.Dispose(disposing);
    }
}
