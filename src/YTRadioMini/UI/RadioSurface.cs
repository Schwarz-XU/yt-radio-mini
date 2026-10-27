using System.Drawing.Drawing2D;
using System.Drawing.Text;
using YTRadioMini.Models;

namespace YTRadioMini.UI;

/// <summary>
/// 整台"收音机"的自绘表面：外壳、LCD、扬声器格栅、两个圆形按钮和状态灯。
/// 所有坐标以 360 × 240 为基准，绘制时按控件宽度缩放，因此在任何 DPI 下版面一致。
/// 只在状态变化时重绘，没有定时器和动画。
/// </summary>
public sealed class RadioSurface : Control
{
    public const float BaseWidth = 360f;
    public const float BaseHeight = 240f;

    private static readonly RectangleF LcdRect = new(16, 14, 328, 92);
    private static readonly RectangleF GrilleRect = new(18, 122, 150, 56);
    private static readonly RectangleF PlayRect = new(196, 122, 52, 52);
    private static readonly RectangleF NextRect = new(262, 122, 52, 52);
    private static readonly RectangleF LampRect = new(326, 142, 12, 12);

    private readonly Font _positionFont = MakeFont(11, FontStyle.Regular);
    private readonly Font _tagFont = MakeFont(11, FontStyle.Bold);
    private readonly Font _titleFont = MakeFont(14, FontStyle.Bold);
    private readonly Font _messageFont = MakeFont(11, FontStyle.Regular);
    private readonly StringFormat _titleFormat = new(StringFormatFlags.LineLimit) { Trimming = StringTrimming.EllipsisWord };
    private readonly StringFormat _lineFormat = new(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter };
    private readonly StringFormat _rightFormat = new(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisCharacter };

    private RadioViewState _view = RadioViewState.Empty;
    private Button? _pressed;
    private string _inputNote = "";

    private enum Button { Play, Next }

    public RadioSurface()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false); // 不抢键盘焦点，Space / N 由窗体处理
        TabStop = false;
        AccessibleName = "YT Radio Mini";
        AccessibleRole = AccessibleRole.Client;
    }

    public event Action? PlayPauseClicked;
    public event Action? NextClicked;

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public RadioViewState View
    {
        get => _view;
        set
        {
            if (_view == value) return;
            _view = value;
            AccessibleDescription = $"{value.State}: {value.Title} {value.Position}/{value.Count} {value.Message}".Trim();
            Invalidate();
        }
    }

    /// <summary>显示在输入框上方的一行小字，例如"3 条链接待提交"；空字符串表示不显示。</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string InputNote
    {
        get => _inputNote;
        set
        {
            if (_inputNote == value) return;
            _inputNote = value;
            Invalidate();
        }
    }

    private float ScaleFactor => Width / BaseWidth;

    /// <summary>基准坐标 → 当前控件坐标。</summary>
    public Rectangle ToClient(RectangleF baseRect)
    {
        var s = ScaleFactor;
        return Rectangle.Round(new RectangleF(baseRect.X * s, baseRect.Y * s, baseRect.Width * s, baseRect.Height * s));
    }

    private static Font MakeFont(float pixels, FontStyle style) =>
        new(RetroTheme.LcdFontFamily, pixels, style, GraphicsUnit.Pixel);

    // ───────────── 绘制 ─────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        try
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(RetroTheme.Shell);
            g.ScaleTransform(ScaleFactor, ScaleFactor);

            DrawShell(g);
            DrawLcd(g, _view);
            DrawGrille(g);
            DrawButton(g, PlayRect, Button.Play, enabled: _view.CanTogglePause);
            DrawButton(g, NextRect, Button.Next, enabled: _view.CanNext);
            DrawLamp(g, _view.State);

            if (_inputNote.Length > 0)
            {
                using var note = new SolidBrush(RetroTheme.SecondaryText);
                g.DrawString(_inputNote, _messageFont, note, new RectangleF(18, 178, 324, 14), _lineFormat);
            }
        }
        catch (Exception)
        {
            // 绘制失败不应让窗口出现错误提示框；下一次状态变化会重绘。
        }
    }

    private static void DrawShell(Graphics g)
    {
        using var light = new Pen(RetroTheme.Blend(RetroTheme.Shell, Color.White, 0.35f), 2);
        using var dark = new Pen(RetroTheme.Blend(RetroTheme.Shell, Color.Black, 0.25f), 2);
        g.DrawLine(light, 1, 1, BaseWidth - 2, 1);
        g.DrawLine(light, 1, 1, 1, BaseHeight - 2);
        g.DrawLine(dark, 1, BaseHeight - 1, BaseWidth - 1, BaseHeight - 1);
        g.DrawLine(dark, BaseWidth - 1, 1, BaseWidth - 1, BaseHeight - 1);
    }

    private void DrawLcd(Graphics g, RadioViewState v)
    {
        using var bezelPath = RoundedRect(Inflate(LcdRect, 3), 10);
        using var lcdPath = RoundedRect(LcdRect, 7);
        using var bezel = new SolidBrush(RetroTheme.Blend(RetroTheme.Shell, Color.Black, 0.35f));
        using var lcd = new SolidBrush(RetroTheme.LcdBackground);
        g.FillPath(bezel, bezelPath);
        g.FillPath(lcd, lcdPath);

        var text = RetroTheme.LcdText;
        using var normal = new SolidBrush(text);
        using var dim = new SolidBrush(Color.FromArgb(170, text));

        var left = LcdRect.Left + 10;
        var width = LcdRect.Width - 20;

        // 第一行：队列位置 + 状态标记
        var position = v.Count > 0 ? $"{v.Position:00} / {v.Count:00}" : "-- / --";
        g.DrawString(position, _positionFont, dim, new RectangleF(left, LcdRect.Top + 6, 110, 16), _lineFormat);

        var tag = TagFor(v);
        if (tag.Length > 0)
            g.DrawString(tag, _tagFont, normal, new RectangleF(left + 110, LcdRect.Top + 6, width - 110, 16), _rightFormat);

        // 标题：最多两行，超出部分截断
        g.DrawString(v.Title, _titleFont, normal, new RectangleF(left, LcdRect.Top + 26, width, 38), _titleFormat);

        // 最后一行：简短的错误或提示
        if (!string.IsNullOrEmpty(v.Message))
            g.DrawString(v.Message, _messageFont, dim, new RectangleF(left, LcdRect.Bottom - 22, width, 16), _lineFormat);
    }

    private static string TagFor(RadioViewState v) =>
        v.State is PlaybackState.Loading or PlaybackState.Resolving ? "LOADING..."
        : v.State == PlaybackState.Error ? "ERROR"
        : v.IsLive ? "LIVE"
        : "";

    private static void DrawGrille(Graphics g)
    {
        using var slot = new SolidBrush(RetroTheme.Blend(RetroTheme.Shell, Color.Black, 0.28f));
        using var highlight = new SolidBrush(RetroTheme.Blend(RetroTheme.Shell, Color.White, 0.3f));
        const int rows = 6;
        var step = GrilleRect.Height / rows;
        for (var i = 0; i < rows; i++)
        {
            var y = GrilleRect.Top + i * step + 2;
            using var path = RoundedRect(new RectangleF(GrilleRect.Left, y, GrilleRect.Width, 4), 2);
            g.FillPath(slot, path);
            g.FillRectangle(highlight, GrilleRect.Left + 2, y + 5, GrilleRect.Width - 4, 1);
        }
    }

    private void DrawButton(Graphics g, RectangleF rect, Button which, bool enabled)
    {
        var pressed = _pressed == which && enabled;
        var r = pressed ? new RectangleF(rect.X, rect.Y + 1, rect.Width, rect.Height) : rect;

        var fill = pressed ? RetroTheme.Blend(RetroTheme.Button, Color.Black, 0.2f) : RetroTheme.Button;
        using var shadow = new SolidBrush(RetroTheme.Blend(RetroTheme.Shell, Color.Black, 0.3f));
        using var body = new SolidBrush(fill);
        using var rim = new Pen(RetroTheme.Blend(RetroTheme.Button, Color.Black, 0.45f), 2);

        if (!pressed) g.FillEllipse(shadow, r.X, r.Y + 3, r.Width, r.Height);
        g.FillEllipse(body, r);
        g.DrawEllipse(rim, r);

        var icon = enabled ? RetroTheme.LcdText : RetroTheme.Blend(RetroTheme.Button, RetroTheme.LcdText, 0.3f);
        using var iconBrush = new SolidBrush(icon);
        var cx = r.X + r.Width / 2;
        var cy = r.Y + r.Height / 2;

        if (which == Button.Play)
        {
            if (_view.State == PlaybackState.Playing)
            {
                g.FillRectangle(iconBrush, cx - 9, cy - 11, 6, 22);
                g.FillRectangle(iconBrush, cx + 3, cy - 11, 6, 22);
            }
            else
            {
                g.FillPolygon(iconBrush, new[] { new PointF(cx - 8, cy - 12), new PointF(cx - 8, cy + 12), new PointF(cx + 12, cy) });
            }
        }
        else
        {
            g.FillPolygon(iconBrush, new[] { new PointF(cx - 11, cy - 11), new PointF(cx - 11, cy + 11), new PointF(cx + 5, cy) });
            g.FillRectangle(iconBrush, cx + 7, cy - 11, 4, 22);
        }
    }

    private static void DrawLamp(Graphics g, PlaybackState state)
    {
        var color = state switch
        {
            PlaybackState.Playing => RetroTheme.LampGreen,
            PlaybackState.Loading or PlaybackState.Resolving => RetroTheme.LampAmber,
            _ => RetroTheme.LampGray, // 待机、暂停、停止、出错
        };
        using var ring = new SolidBrush(RetroTheme.Blend(RetroTheme.Shell, Color.Black, 0.4f));
        using var body = new SolidBrush(color);
        using var glint = new SolidBrush(Color.FromArgb(90, Color.White));
        g.FillEllipse(ring, Inflate(LampRect, 2));
        g.FillEllipse(body, LampRect);
        g.FillEllipse(glint, LampRect.X + 2.5f, LampRect.Y + 2f, 3.5f, 3.5f);
    }

    private static RectangleF Inflate(RectangleF r, float d) => RectangleF.Inflate(r, d, d);

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // ───────────── 鼠标 ─────────────

    private Button? HitTest(Point p)
    {
        var s = ScaleFactor;
        var x = p.X / s;
        var y = p.Y / s;
        if (Contains(PlayRect, x, y)) return Button.Play;
        if (Contains(NextRect, x, y)) return Button.Next;
        return null;
    }

    /// <summary>用外接圆做命中判断，使点击区域与圆形按钮一致。</summary>
    private static bool Contains(RectangleF r, float x, float y)
    {
        var dx = x - (r.X + r.Width / 2);
        var dy = y - (r.Y + r.Height / 2);
        return dx * dx + dy * dy <= r.Width * r.Width / 4;
    }

    private bool IsEnabled(Button b) => b == Button.Play ? _view.CanTogglePause : _view.CanNext;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        // 点击机身时让输入框失去焦点，Space / N 快捷键随即恢复可用。
        if (FindForm() is { } form) form.ActiveControl = null;
        if (e.Button != MouseButtons.Left) return;
        if (HitTest(e.Location) is { } b && IsEnabled(b))
        {
            _pressed = b;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var pressed = _pressed;
        _pressed = null;
        if (pressed is null) return;
        Invalidate();

        if (HitTest(e.Location) == pressed && IsEnabled(pressed.Value))
        {
            if (pressed == Button.Play) PlayPauseClicked?.Invoke();
            else NextClicked?.Invoke();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var over = HitTest(e.Location) is { } b && IsEnabled(b);
        Cursor = over ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_pressed is not null)
        {
            _pressed = null;
            Invalidate();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _positionFont.Dispose();
            _tagFont.Dispose();
            _titleFont.Dispose();
            _messageFont.Dispose();
            _titleFormat.Dispose();
            _lineFormat.Dispose();
            _rightFormat.Dispose();
        }
        base.Dispose(disposing);
    }
}
