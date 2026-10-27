using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using YTRadioMini.AppLogic;
using YTRadioMini.Models;

namespace YTRadioMini.UI;

/// <summary>
/// 整台"收音机"的自绘表面：外壳、LCD、扬声器格栅、三个圆形按钮和状态灯。
/// LCD 有两种显示：正常的播放信息，和队列列表（用来选择、删除、移动条目）。
/// 所有坐标以 360 × 240 为基准，绘制时按控件宽度缩放，因此在任何 DPI 下版面一致。
/// 只在状态变化时重绘，没有定时器和动画。
/// </summary>
public sealed class RadioSurface : Control
{
    public const float BaseWidth = 360f;
    public const float BaseHeight = 240f;

    private const int ListRows = 4;
    private const float ListHeaderHeight = 24f;   // 相对 LCD 内框顶部：列表第一行的起点
    private const float ListRowHeight = 15f;
    public const float WindowRadius = 14f;

    private static readonly RectangleF LcdRect = new(14, 20, 332, 92);      // 黑色边框，整块 LCD 的点击区域
    private static readonly RectangleF LcdInner = new(18, 24, 324, 84);     // 荧光显示区
    private static readonly RectangleF GrilleRect = new(18, 126, 104, 52);
    private static readonly RectangleF PrevRect = new(136, 126, 52, 52);
    private static readonly RectangleF PlayRect = new(198, 126, 52, 52);
    private static readonly RectangleF NextRect = new(260, 126, 52, 52);
    private static readonly RectangleF LampRing = new(324, 144, 16, 16);
    private static readonly RectangleF LampBody = new(326, 146, 12, 12);
    private static readonly PointF MinimizeCenter = new(322.5f, 9.5f);
    private static readonly PointF CloseCenter = new(341.5f, 9.5f);
    private const float WindowButtonDiameter = 13f;
    private const float WindowButtonHitRadius = 10f;
    private static readonly RectangleF NoteRect = new(18, 181, 324, 14);

    /// <summary>输入框在窗口中的位置（基准坐标），由窗体用来摆放文本框。</summary>
    public static readonly RectangleF InputRect = new(16, 197, 328, 28);

    private readonly Font _positionFont = MakeFont(11, FontStyle.Regular);
    private readonly Font _tagFont = MakeFont(11, FontStyle.Bold);
    private readonly Font _titleFont = MakeFont(14, FontStyle.Bold);
    private readonly Font _messageFont = MakeFont(11, FontStyle.Regular);
    private readonly Font _rowFont = MakeFont(12, FontStyle.Regular);
    private readonly StringFormat _titleFormat = new(StringFormatFlags.LineLimit) { Trimming = StringTrimming.EllipsisWord };
    private readonly StringFormat _lineFormat = new(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter };
    private readonly StringFormat _rightFormat = new(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisCharacter };

    private readonly QueueBrowser _browser = new(ListRows);
    private RadioViewState _view = RadioViewState.Empty;
    private Button? _pressed;
    private string _inputNote = "";
    private bool _listMode;

    private enum Button { Previous, Play, Next, Minimize, Close }

    public RadioSurface()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false); // 不抢键盘焦点，快捷键由窗体处理
        TabStop = false;
        AccessibleName = "YT Radio Mini";
        AccessibleRole = AccessibleRole.Client;
    }

    public event Action? MinimizeClicked;
    public event Action? CloseClicked;

    /// <summary>在机身空白处按下鼠标：窗体据此开始拖动窗口。</summary>
    public event Action? DragRequested;

    public event Action? PreviousClicked;
    public event Action? PlayPauseClicked;
    public event Action? NextClicked;

    /// <summary>在队列列表中选择了某一项播放。</summary>
    public event Action<int>? PlayIndexRequested;

    public event Action<int>? RemoveRequested;
    public event Action<int, int>? MoveRequested;
    public event Action? ClearRequested;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RadioViewState View
    {
        get => _view;
        set
        {
            if (_view == value) return;
            _view = value;
            if (_listMode)
            {
                _browser.SetCount(value.Queue?.Count ?? 0);
                if (_browser.Count == 0) _listMode = false; // 队列被清空：回到播放显示
            }
            AccessibleDescription = $"{value.State}: {value.Title} {value.Position}/{value.Count} {value.Message}".Trim();
            Invalidate();
        }
    }

    /// <summary>显示在输入框上方的一行小字，例如"3 LINKS READY"；空字符串表示不显示。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
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

    [Browsable(false)]
    public bool ListMode => _listMode;

    private float ScaleFactor => Width / BaseWidth;

    /// <summary>基准坐标 → 当前控件坐标。</summary>
    public Rectangle ToClient(RectangleF baseRect)
    {
        var s = ScaleFactor;
        return Rectangle.Round(new RectangleF(baseRect.X * s, baseRect.Y * s, baseRect.Width * s, baseRect.Height * s));
    }

    private static Font MakeFont(float pixels, FontStyle style) =>
        new(RetroTheme.LcdFontFamily, pixels, style, GraphicsUnit.Pixel);

    // ───────────── 队列列表 ─────────────

    private int QueueCount => _view.Queue?.Count ?? 0;

    public void OpenList()
    {
        if (QueueCount == 0) return;
        _browser.Open(Math.Max(0, _view.Position - 1), QueueCount);
        _listMode = true;
        Invalidate();
    }

    public void CloseList()
    {
        if (!_listMode) return;
        _listMode = false;
        Invalidate();
    }

    /// <summary>
    /// 处理与队列列表有关的按键，返回是否已处理。
    /// 未打开列表时：L、↑、↓、PageUp、PageDown 打开列表。
    /// 打开后：↑/↓/PageUp/PageDown/Home/End 移动光标，Enter 播放光标项，Delete 删除光标项，
    /// Shift+Delete 清空队列，Ctrl+↑/Ctrl+↓ 上移/下移光标项，Esc 或 L 关闭列表。
    /// </summary>
    public bool HandleKey(Keys key, Keys modifiers)
    {
        if (!_listMode)
        {
            if (modifiers != Keys.None) return false;
            if (key is Keys.L or Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown)
            {
                OpenList();
                return true;
            }
            return false;
        }

        var ctrl = modifiers == Keys.Control;
        var shift = modifiers == Keys.Shift;
        var none = modifiers == Keys.None;

        switch (key)
        {
            case Keys.Up when none: _browser.MoveBy(-1); break;
            case Keys.Down when none: _browser.MoveBy(1); break;
            case Keys.PageUp when none: _browser.MoveBy(-ListRows); break;
            case Keys.PageDown when none: _browser.MoveBy(ListRows); break;
            case Keys.Home when none: _browser.Home(); break;
            case Keys.End when none: _browser.End(); break;

            case Keys.Up when ctrl: MoveCursorItem(-1); break;
            case Keys.Down when ctrl: MoveCursorItem(1); break;

            case Keys.Enter when none:
                if (QueueCount > 0) PlayIndexRequested?.Invoke(_browser.Cursor);
                CloseList();
                break;

            case Keys.Delete when none:
                if (QueueCount > 0) RemoveRequested?.Invoke(_browser.Cursor);
                break;

            case Keys.Delete when shift:
                ClearRequested?.Invoke();
                CloseList();
                break;

            case Keys.Escape when none:
            case Keys.L when none:
                CloseList();
                break;

            default:
                return false;
        }

        Invalidate();
        return true;
    }

    private void MoveCursorItem(int delta)
    {
        var from = _browser.Cursor;
        var to = from + delta;
        if (to < 0 || to >= QueueCount) return;
        MoveRequested?.Invoke(from, to);
        _browser.SetCursor(to); // 光标跟着条目走，不等控制器回传
    }

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
            DrawWindowButton(g, MinimizeCenter, Button.Minimize);
            DrawWindowButton(g, CloseCenter, Button.Close);
            if (_listMode) DrawQueueLcd(g, _view);
            else DrawLcd(g, _view);
            DrawGrille(g);
            DrawButton(g, PrevRect, Button.Previous, enabled: _view.CanPrevious);
            DrawButton(g, PlayRect, Button.Play, enabled: _view.CanTogglePause);
            DrawButton(g, NextRect, Button.Next, enabled: _view.CanNext);
            DrawLamp(g, _view.State);

            if (_inputNote.Length > 0)
            {
                using var note = new SolidBrush(RetroTheme.NoteText);
                g.DrawString(_inputNote, _messageFont, note, NoteRect, _lineFormat);
            }
        }
        catch (Exception)
        {
            // 绘制失败不应让窗口出现错误提示框；下一次状态变化会重绘。
        }
    }

    /// <summary>外壳：圆角轮廓，顶部一道高光，底部一道阴影。</summary>
    private static void DrawShell(Graphics g)
    {
        const float inset = 0.5f;
        var outline = new RectangleF(inset, inset, BaseWidth - 1, BaseHeight - 1);
        using var path = RoundedRect(outline, WindowRadius);
        using var edge = new Pen(RetroTheme.ShellEdge, 1);
        using var light = new Pen(RetroTheme.ShellHighlight, 1);
        using var shade = new Pen(RetroTheme.ShellShadow, 2);

        g.DrawLine(light, WindowRadius, 1.5f, BaseWidth - WindowRadius, 1.5f);
        g.DrawLine(shade, WindowRadius, BaseHeight - 2.5f, BaseWidth - WindowRadius, BaseHeight - 2.5f);
        g.DrawPath(edge, path);
    }

    private static void DrawLcdFrame(Graphics g)
    {
        using var bezelPath = RoundedRect(LcdRect, 12);
        using var innerPath = RoundedRect(LcdInner, 8);
        using var bezel = new SolidBrush(RetroTheme.LcdBezel);
        using var bezelLight = new Pen(RetroTheme.LcdBezelHighlight, 1);
        using var lcd = new SolidBrush(RetroTheme.LcdBackground);
        using var inset = new Pen(RetroTheme.LcdShadow, 2);

        g.FillPath(bezel, bezelPath);
        g.DrawLine(bezelLight, LcdRect.Left + 12, LcdRect.Top + 0.5f, LcdRect.Right - 12, LcdRect.Top + 0.5f);
        g.FillPath(lcd, innerPath);
        g.DrawLine(inset, LcdInner.Left + 8, LcdInner.Top + 1, LcdInner.Right - 8, LcdInner.Top + 1); // 凹进去的上沿阴影
    }

    private void DrawLcd(Graphics g, RadioViewState v)
    {
        DrawLcdFrame(g);

        using var normal = new SolidBrush(RetroTheme.LcdText);
        using var dim = new SolidBrush(RetroTheme.LcdDim);

        var left = LcdInner.Left + 10;
        var width = LcdInner.Width - 20;

        // 第一行：队列位置 + 状态标记
        var position = v.Count > 0 ? $"{v.Position:00} / {v.Count:00}" : "-- / --";
        g.DrawString(position, _positionFont, dim, new RectangleF(left, LcdInner.Top + 7, 110, 16), _lineFormat);

        var tag = TagFor(v);
        if (tag.Length > 0)
            g.DrawString(tag, _tagFont, normal, new RectangleF(left + 110, LcdInner.Top + 7, width - 110, 16), _rightFormat);

        // 标题：最多两行，超出部分截断
        g.DrawString(v.Title, _titleFont, normal, new RectangleF(left, LcdInner.Top + 27, width, 36), _titleFormat);

        // 最后一行：简短的错误或提示
        if (!string.IsNullOrEmpty(v.Message))
            g.DrawString(v.Message, _messageFont, dim, new RectangleF(left, LcdInner.Top + 65, width, 16), _lineFormat);
    }

    private void DrawQueueLcd(Graphics g, RadioViewState v)
    {
        DrawLcdFrame(g);

        using var normal = new SolidBrush(RetroTheme.LcdText);
        using var dim = new SolidBrush(RetroTheme.LcdDim);
        using var inverse = new SolidBrush(RetroTheme.LcdBackground);

        var left = LcdInner.Left + 10;
        var width = LcdInner.Width - 20;
        var items = v.Queue ?? Array.Empty<QueueItem>();

        g.DrawString($"QUEUE {(items.Count == 0 ? 0 : _browser.Cursor + 1):00}/{items.Count:00}", _positionFont, dim,
            new RectangleF(left, LcdInner.Top + 7, 110, 16), _lineFormat);
        g.DrawString("ENTER:PLAY DEL:REMOVE", _messageFont, dim,
            new RectangleF(left + 110, LcdInner.Top + 7, width - 110, 16), _rightFormat);

        var playing = v.Position - 1;
        for (var row = 0; row < ListRows; row++)
        {
            var index = _browser.IndexAtRow(row);
            if (index < 0 || index >= items.Count) break;

            var item = items[index];
            var selected = index == _browser.Cursor;
            var rowRect = new RectangleF(LcdInner.Left + 4, LcdInner.Top + ListHeaderHeight + row * ListRowHeight, LcdInner.Width - 8, ListRowHeight);

            if (selected)
            {
                using var path = RoundedRect(rowRect, 2);
                g.FillPath(normal, path);
            }

            var label = string.IsNullOrWhiteSpace(item.Title) ? $"({item.Id})" : item.Title;
            var marker = index == playing ? '>' : ' ';
            g.DrawString($"{marker}{index + 1:000} {label}", _rowFont, selected ? inverse : normal,
                new RectangleF(rowRect.X + 6, rowRect.Y, rowRect.Width - 12, rowRect.Height), _lineFormat);
        }
    }

    private static string TagFor(RadioViewState v) =>
        v.State is PlaybackState.Loading or PlaybackState.Resolving ? "LOADING..."
        : v.State == PlaybackState.Error ? "ERROR"
        : v.IsLive ? "LIVE"
        : "";

    private static void DrawGrille(Graphics g)
    {
        using var slot = new SolidBrush(RetroTheme.Slot);
        using var highlight = new SolidBrush(RetroTheme.ShellHighlight);
        const int rows = 6;
        var step = (GrilleRect.Height - 4) / (rows - 1);
        for (var i = 0; i < rows; i++)
        {
            var y = GrilleRect.Top + i * step;
            g.FillRectangle(highlight, GrilleRect.Left + 2, y + 4, GrilleRect.Width - 4, 1);
            using var path = RoundedRect(new RectangleF(GrilleRect.Left, y, GrilleRect.Width, 4), 2);
            g.FillPath(slot, path);
        }
    }

    private void DrawButton(Graphics g, RectangleF rect, Button which, bool enabled)
    {
        var pressed = _pressed == which && enabled;
        var r = pressed ? new RectangleF(rect.X, rect.Y + 1, rect.Width, rect.Height) : rect;

        using var shadow = new SolidBrush(RetroTheme.Slot);
        using var body = new SolidBrush(pressed ? RetroTheme.Blend(RetroTheme.Button, Color.Black, 0.2f) : RetroTheme.Button);
        using var ring = new Pen(enabled ? RetroTheme.Brass : RetroTheme.ButtonDisabled, 2);

        if (!pressed) g.FillEllipse(shadow, r.X, r.Y + 3, r.Width, r.Height);
        g.FillEllipse(body, r);
        g.DrawEllipse(ring, RectangleF.Inflate(r, -1, -1));

        using var icon = new SolidBrush(enabled ? RetroTheme.ButtonIcon : RetroTheme.ButtonDisabled);
        var cx = r.X + r.Width / 2;
        var cy = r.Y + r.Height / 2;

        switch (which)
        {
            case Button.Play when _view.State == PlaybackState.Playing:
                g.FillRectangle(icon, cx - 8, cy - 11, 6, 22);
                g.FillRectangle(icon, cx + 2, cy - 11, 6, 22);
                break;

            case Button.Play:
                g.FillPolygon(icon, new[] { new PointF(cx - 6, cy - 12), new PointF(cx - 6, cy + 12), new PointF(cx + 12, cy) });
                break;

            case Button.Next:
                g.FillPolygon(icon, new[] { new PointF(cx - 7, cy - 10), new PointF(cx - 7, cy + 10), new PointF(cx + 7, cy) });
                g.FillRectangle(icon, cx + 7, cy - 10, 3, 20);
                break;

            case Button.Previous:
                g.FillPolygon(icon, new[] { new PointF(cx + 7, cy - 10), new PointF(cx + 7, cy + 10), new PointF(cx - 7, cy) });
                g.FillRectangle(icon, cx - 10, cy - 10, 3, 20);
                break;
        }
    }

    /// <summary>右上角的最小化与关闭：黄铜边的小圆钮，跟机身上的按钮同一语言。</summary>
    private void DrawWindowButton(Graphics g, PointF center, Button which)
    {
        var pressed = _pressed == which;
        var r = new RectangleF(center.X - WindowButtonDiameter / 2, center.Y - WindowButtonDiameter / 2 + (pressed ? 0.5f : 0),
            WindowButtonDiameter, WindowButtonDiameter);

        using var body = new SolidBrush(pressed ? RetroTheme.Blend(RetroTheme.Button, Color.Black, 0.25f) : RetroTheme.Button);
        using var ring = new Pen(RetroTheme.Brass, 1);
        using var glyph = new Pen(RetroTheme.ButtonIcon, 1.5f);

        g.FillEllipse(body, r);
        g.DrawEllipse(ring, RectangleF.Inflate(r, -0.5f, -0.5f));

        var cx = r.X + r.Width / 2;
        var cy = r.Y + r.Height / 2;
        if (which == Button.Minimize)
        {
            g.DrawLine(glyph, cx - 3, cy, cx + 3, cy);
        }
        else
        {
            g.DrawLine(glyph, cx - 3, cy - 3, cx + 3, cy + 3);
            g.DrawLine(glyph, cx - 3, cy + 3, cx + 3, cy - 3);
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
        using var ring = new SolidBrush(RetroTheme.LcdBezel);
        using var body = new SolidBrush(color);
        using var glint = new SolidBrush(Color.FromArgb(120, Color.White));
        g.FillEllipse(ring, LampRing);
        g.FillEllipse(body, LampBody);
        g.FillEllipse(glint, LampBody.X + 2f, LampBody.Y + 2f, 4f, 4f);
    }

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
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

    private PointF ToBase(Point p) => new(p.X / ScaleFactor, p.Y / ScaleFactor);

    private Button? HitTestButton(Point p)
    {
        var b = ToBase(p);
        if (Near(MinimizeCenter, b, WindowButtonHitRadius)) return Button.Minimize;
        if (Near(CloseCenter, b, WindowButtonHitRadius)) return Button.Close;
        if (InCircle(PrevRect, b)) return Button.Previous;
        if (InCircle(PlayRect, b)) return Button.Play;
        if (InCircle(NextRect, b)) return Button.Next;
        return null;
    }

    private static bool Near(PointF center, PointF p, float radius)
    {
        var dx = p.X - center.X;
        var dy = p.Y - center.Y;
        return dx * dx + dy * dy <= radius * radius;
    }

    /// <summary>用外接圆做命中判断，使点击区域与圆形按钮一致。</summary>
    private static bool InCircle(RectangleF r, PointF p) =>
        Near(new PointF(r.X + r.Width / 2, r.Y + r.Height / 2), p, r.Width / 2);

    /// <summary>列表模式下，鼠标位置对应的队列索引；不在条目行上时返回 -1。</summary>
    private int ListIndexAt(Point p)
    {
        var b = ToBase(p);
        if (!LcdInner.Contains(b)) return -1;
        var row = (int)Math.Floor((b.Y - LcdInner.Top - ListHeaderHeight) / ListRowHeight);
        return row < 0 ? -1 : _browser.IndexAtRow(row);
    }

    private bool IsEnabled(Button b) => b switch
    {
        Button.Previous => _view.CanPrevious,
        Button.Play => _view.CanTogglePause,
        Button.Next => _view.CanNext,
        _ => true, // 最小化、关闭
    };

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        // 点击机身时让输入框失去焦点，快捷键随即恢复可用。
        if (FindForm() is { } form) form.ActiveControl = null;
        if (e.Button != MouseButtons.Left) return;

        if (HitTestButton(e.Location) is { } b)
        {
            if (!IsEnabled(b)) return;
            _pressed = b;
            Invalidate();
            return;
        }

        if (LcdRect.Contains(ToBase(e.Location)))
        {
            if (!_listMode) OpenList();
            else if (ListIndexAt(e.Location) is var index and >= 0) { _browser.SetCursor(index); Invalidate(); }
            else if (ToBase(e.Location).Y < LcdInner.Top + ListHeaderHeight) CloseList(); // 点击标题栏：返回播放显示
            return;
        }

        DragRequested?.Invoke(); // 机身上的其余位置：按住拖动窗口
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (!_listMode || e.Button != MouseButtons.Left) return;

        if (ListIndexAt(e.Location) is var index and >= 0)
        {
            PlayIndexRequested?.Invoke(index);
            CloseList();
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!_listMode || e.Delta == 0) return;
        _browser.MoveBy(e.Delta > 0 ? -1 : 1);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var pressed = _pressed;
        _pressed = null;
        if (pressed is null) return;
        Invalidate();

        if (HitTestButton(e.Location) == pressed && IsEnabled(pressed.Value))
        {
            switch (pressed)
            {
                case Button.Previous: PreviousClicked?.Invoke(); break;
                case Button.Play: PlayPauseClicked?.Invoke(); break;
                case Button.Next: NextClicked?.Invoke(); break;
                case Button.Minimize: MinimizeClicked?.Invoke(); break;
                case Button.Close: CloseClicked?.Invoke(); break;
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var overButton = HitTestButton(e.Location) is { } b && IsEnabled(b);
        var overLcd = LcdRect.Contains(ToBase(e.Location)) && (_listMode || QueueCount > 0);
        Cursor = overButton || overLcd ? Cursors.Hand : Cursors.Default;
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
            _rowFont.Dispose();
            _titleFormat.Dispose();
            _lineFormat.Dispose();
            _rightFormat.Dispose();
        }
        base.Dispose(disposing);
    }
}
