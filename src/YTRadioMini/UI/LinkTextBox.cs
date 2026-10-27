using System.Runtime.InteropServices;

namespace YTRadioMini.UI;

/// <summary>
/// 链接输入框。粘贴时统一换行符：Win32 多行编辑框只把 CR LF 当作换行，
/// 剪贴板里只有 LF 或只有 CR 的文本（常见于网页、部分编辑器）会显示成一行，所以先规范化再插入。
/// </summary>
public sealed class LinkTextBox : TextBox
{
    private const int WM_PASTE = 0x0302;

    /// <summary>粘贴剪贴板文本（换行规范化为 CR LF），包括右键菜单和 Ctrl+V 触发的粘贴。</summary>
    public new void Paste() => PasteNormalized();

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_PASTE && !ReadOnly)
        {
            PasteNormalized();
            return;
        }
        base.WndProc(ref m);
    }

    private void PasteNormalized()
    {
        string text;
        try
        {
            if (!Clipboard.ContainsText()) return;
            text = Clipboard.GetText();
        }
        catch (ExternalException)
        {
            return; // 剪贴板正被其他程序占用
        }

        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");

        var room = MaxLength - (TextLength - SelectionLength);
        if (room <= 0) return;
        if (text.Length > room) text = text[..room];

        SelectedText = text;
    }

    /// <summary>当前内容的非空行数，用于判断是否是多行输入。</summary>
    public int LineCountNonEmpty =>
        Text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries).Count(l => l.Trim().Length > 0);
}
