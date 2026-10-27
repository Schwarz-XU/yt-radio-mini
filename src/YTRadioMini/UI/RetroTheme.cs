using System.Drawing;

namespace YTRadioMini.UI;

/// <summary>
/// 配色与字体常量，对应设计稿（Claude Design 画布 "YT Radio Mini GUI"）：
/// 米色胶木外壳、深胡桃木色按钮配黄铜环、荧光绿 LCD。
/// </summary>
public static class RetroTheme
{
    // 外壳
    public static readonly Color Shell = ColorTranslator.FromHtml("#DCCBA2");
    public static readonly Color ShellHighlight = ColorTranslator.FromHtml("#F0E4C6");
    public static readonly Color ShellShadow = ColorTranslator.FromHtml("#B29E74");
    public static readonly Color ShellEdge = ColorTranslator.FromHtml("#8C7852");
    public static readonly Color Slot = ColorTranslator.FromHtml("#A28D62");

    // LCD
    public static readonly Color LcdBezel = ColorTranslator.FromHtml("#3B2F22");
    public static readonly Color LcdBezelHighlight = ColorTranslator.FromHtml("#54432F");
    public static readonly Color LcdBackground = ColorTranslator.FromHtml("#19281F");
    public static readonly Color LcdShadow = ColorTranslator.FromHtml("#0A120D");
    public static readonly Color LcdText = ColorTranslator.FromHtml("#C8F59B");
    public static readonly Color LcdDim = ColorTranslator.FromHtml("#7FA66A");

    // 按钮（含窗口控制按钮）
    public static readonly Color Button = ColorTranslator.FromHtml("#5B3D28");
    public static readonly Color Brass = ColorTranslator.FromHtml("#C29A55");
    public static readonly Color ButtonIcon = ColorTranslator.FromHtml("#F3E7C8");
    public static readonly Color ButtonDisabled = ColorTranslator.FromHtml("#8B7358");

    // 状态灯
    public static readonly Color LampGreen = ColorTranslator.FromHtml("#3FBF5F");
    public static readonly Color LampAmber = ColorTranslator.FromHtml("#E0A030");
    public static readonly Color LampGray = ColorTranslator.FromHtml("#8D866F");

    // 输入框与其上方的提示
    public static readonly Color InputBackground = ColorTranslator.FromHtml("#F1E8D0");
    public static readonly Color InputText = ColorTranslator.FromHtml("#4A3A26");
    public static readonly Color InputHint = ColorTranslator.FromHtml("#6E5E40");
    public static readonly Color NoteText = ColorTranslator.FromHtml("#5E4E34");

    public const string LcdFontFamily = "Consolas";

    /// <summary>在两种颜色之间线性混合，amount 为 0 时是 a，为 1 时是 b。</summary>
    public static Color Blend(Color a, Color b, float amount)
    {
        int Mix(int x, int y) => (int)Math.Round(x + (y - x) * amount);
        return Color.FromArgb(Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B));
    }
}
