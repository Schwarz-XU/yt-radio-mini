using YTRadioMini.AppLogic;
using YTRadioMini.Models;

namespace YTRadioMini.UI;

/// <summary>
/// 主窗口：只做显示和转发输入。播放状态全部来自 <see cref="IRadioController"/>，
/// 窗口本身不保存任何播放状态，也不直接接触 mpv 或 yt-dlp。
/// </summary>
public sealed class MainForm : Form
{
    private const string Hint = "Paste YouTube link, press Enter";

    private readonly IRadioController _radio;
    private readonly RadioSurface _surface = new() { Dock = DockStyle.Fill };
    private readonly TextBox _input = new();
    private Font? _inputFont;
    private bool _hintShown;

    public MainForm(IRadioController radio)
    {
        _radio = radio;

        Text = "YT Radio Mini";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
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
        _input.AccessibleName = "YouTube link";

        Controls.Add(_input);
        Controls.Add(_surface);
        _input.BringToFront();

        _surface.PlayPauseClicked += () => Fire(_radio.TogglePauseAsync);
        _surface.NextClicked += () => Fire(_radio.NextAsync);
        _surface.SizeChanged += (_, _) => LayoutInput();
        _input.KeyDown += OnInputKeyDown;
        _input.GotFocus += (_, _) => HideHint();
        _input.Leave += (_, _) => ShowHintIfEmpty();

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
    }

    private void LayoutInput()
    {
        var s = _surface.Width / RadioSurface.BaseWidth;
        _input.Bounds = _surface.ToClient(new RectangleF(16, 194, 328, 28));

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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _input.Focused) return; // 输入框有焦点时，Space / N 属于输入框

        switch (e.KeyCode)
        {
            case Keys.Space when e.Modifiers == Keys.None:
                e.SuppressKeyPress = e.Handled = true;
                Fire(_radio.TogglePauseAsync);
                break;

            case Keys.N when e.Modifiers == Keys.None:
                e.SuppressKeyPress = e.Handled = true;
                Fire(_radio.NextAsync);
                break;

            case Keys.V when e.Modifiers == Keys.Control:
                // 没有焦点时 Ctrl+V 直接把剪贴板内容送进输入框。
                e.SuppressKeyPress = e.Handled = true;
                _input.Focus();
                _input.Paste();
                break;
        }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            // Enter 与 Ctrl+Enter 都提交输入框中的全部内容（单行或多行）。
            e.SuppressKeyPress = e.Handled = true;
            Submit();
        }
        else if (e.KeyCode == Keys.Escape)
        {
            e.SuppressKeyPress = e.Handled = true;
            ActiveControl = null; // 放弃输入，把焦点还给窗口，Space / N 恢复可用
        }
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
        _input.ForeColor = RetroTheme.SecondaryText;
    }

    private void ShowHintIfEmpty()
    {
        if (_input.Focused || _input.TextLength > 0) return;
        _hintShown = true;
        _input.ForeColor = RetroTheme.Blend(RetroTheme.SecondaryText, RetroTheme.InputBackground, 0.45f);
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
