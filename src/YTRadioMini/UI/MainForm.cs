using YTRadioMini.AppLogic;
using YTRadioMini.Models;

namespace YTRadioMini.UI;

// 阶段 0 占位：仅用于验证 UI → IRadioController 的接线；复古外观由 Agent 1 实现。
public sealed class MainForm : Form
{
    private readonly IRadioController _radio;
    private readonly Label _lcd = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };

    public MainForm(IRadioController radio)
    {
        _radio = radio;

        Text = "YT Radio Mini";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(360, 240);
        BackColor = RetroTheme.Shell;

        _lcd.BackColor = RetroTheme.LcdBackground;
        _lcd.ForeColor = RetroTheme.LcdText;
        Controls.Add(_lcd);

        _radio.StateChanged += OnStateChanged;
        Render(_radio.State);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _radio.StateChanged -= OnStateChanged;
        base.Dispose(disposing);
    }

    private void OnStateChanged(RadioViewState state)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(() => Render(state)); }
            catch (InvalidOperationException) { /* 窗口正在关闭 */ }
        }
        else Render(state);
    }

    private void Render(RadioViewState s) =>
        _lcd.Text = $"{s.State}\n{s.Title}\n{s.Position:00} / {s.Count:00}\n{s.Message}";
}
