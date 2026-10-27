using System.Drawing;

namespace YTRadioMini.UI;

/// <summary>配色与字体常量。颜色取自方案 2.1；方案没有给出的颜色（灯的灰色与琥珀色、输入框底色）标注在各自旁边。</summary>
public static class RetroTheme
{
    public static readonly Color Shell = ColorTranslator.FromHtml("#C9B18C");
    public static readonly Color Button = ColorTranslator.FromHtml("#5C4835");
    public static readonly Color LcdBackground = ColorTranslator.FromHtml("#26382E");
    public static readonly Color LcdText = ColorTranslator.FromHtml("#E0F2C9");
    public static readonly Color SecondaryText = ColorTranslator.FromHtml("#66513A");
    public static readonly Color LampGreen = ColorTranslator.FromHtml("#2F7A4A");

    // 方案未指定：
    public static readonly Color LampGray = ColorTranslator.FromHtml("#8A8578");
    public static readonly Color LampAmber = ColorTranslator.FromHtml("#D9A441");
    public static readonly Color InputBackground = ColorTranslator.FromHtml("#E6D8BC");

    public const string LcdFontFamily = "Consolas";

    /// <summary>在两种颜色之间线性混合，amount 为 0 时是 a，为 1 时是 b。</summary>
    public static Color Blend(Color a, Color b, float amount)
    {
        int Mix(int x, int y) => (int)Math.Round(x + (y - x) * amount);
        return Color.FromArgb(Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B));
    }
}
