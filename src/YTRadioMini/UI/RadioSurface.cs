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
    private const float ListHeaderHeight = 24f;
    private const float ListRowHeight = 15f;

    private static readonly RectangleF LcdRect = new(16, 14, 328, 92);
    private static readonly RectangleF GrilleRect = new(18, 122, 104, 56);
    private static readonly RectangleF PrevRect = new(136, 122, 52, 52);
    private static readonly RectangleF PlayRect = new(198, 122, 52, 52);
    private static readonly RectangleF NextRect = new(260, 122, 52, 52);
    private static readonly RectangleF LampRect = new(326, 142, 12, 12);

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

    private enum Button { Previous, Play, Next }

    public RadioSurface()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false); // 不抢键盘焦点，快捷键由窗体处理
        TabStop = false;
        AccessibleName = "YT Radio Mini";
        AccessibleRole = AccessibleRole.Client;
    }

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
            if (_listMode) DrawQueueLcd(g, _view);
            else DrawLcd(g, _view);
            DrawGrille(g);
            DrawButton(g, PrevRect, Button.Previous, enabled: _view.CanPrevious);
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

    private static void DrawLcdBackground(Graphics g)
    {
        using var bezelPath = RoundedRect(Inflate(LcdRect, 3), 10);
        using var lcdPath = RoundedRect(LcdRect, 7);
        using var bezel = new SolidBrush(RetroTheme.Blend(RetroTheme.Shell, Color.Black, 0.35f));
        using var lcd = new SolidBrush(RetroTheme.LcdBackground);
        g.FillPath(bezel, bezelPath);
        g.FillPath(lcd, lcdPath);
    }

    private void DrawLcd(Graphics g, RadioViewState v)
    {
        DrawLcdBackground(g);

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

    private void DrawQueueLcd(Graphics g, RadioViewState v)
    {
        DrawLcdBackground(g);

        var text = RetroTheme.LcdText;
        using var normal = new SolidBrush(text);
        using var dim = new SolidBrush(Color.FromArgb(170, text));
        using var inverse = new SolidBrush(RetroTheme.LcdBackground);

        var left = LcdRect.Left + 10;
        var width = LcdRect.Width - 20;
        var items = v.Queue ?? Array.Empty<QueueItem>();

        g.DrawString($"QUEUE {(items.Count == 0 ? 0 : _browser.Cursor + 1):00}/{items.Count:00}", _positionFont, dim,
            new RectangleF(left, LcdRect.Top + 6, 110, 16), _lineFormat);
        g.DrawString("ENTER:PLAY DEL:REMOVE", _messageFont, dim,
            new RectangleF(left + 110, LcdRect.Top + 6, width - 110, 16), _rightFormat);

        var playing = v.Position - 1;
        for (var row = 0; row < ListRows; row++)
        {
            var index = _browser.IndexAtRow(row);
            if (index < 0 || index >= items.Count) break;

            var item = items[index];
            var selected = index == _browser.Cursor;
            var rowRect = new RectangleF(LcdRect.Left + 6, LcdRect.Top + ListHeaderHeight + row * ListRowHeight, LcdRect.Width - 12, ListRowHeight);

            if (selected) g.FillRectangle(normal, rowRect);

            var label = string.IsNullOrWhiteSpace(item.Title) ? $"({item.Id})" : item.Title;
            var marker = index == playing ? '>' : ' ';
            g.DrawString($"{marker}{index + 1:000} {label}", _rowFont, selected ? inverse : normal,
                new RectangleF(rowRect.X + 2, rowRect.Y, rowRect.Width - 4, rowRect.Height), _lineFormat);
        }
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

        switch (which)
        {
            case Button.Play when _view.State == PlaybackState.Playing:
                g.FillRectangle(iconBrush, cx - 9, cy - 11, 6, 22);
                g.FillRectangle(iconBrush, cx + 3, cy - 11, 6, 22);
                break;

            case Button.Play:
                g.FillPolygon(iconBrush, new[] { new PointF(cx - 8, cy - 12), new PointF(cx - 8, cy + 12), new PointF(cx + 12, cy) });
                break;

            case Button.Next:
                g.FillPolygon(iconBrush, new[] { new PointF(cx - 11, cy - 11), new PointF(cx - 11, cy + 11), new PointF(cx + 5, cy) });
                g.FillRectangle(iconBrush, cx + 7, cy - 11, 4, 22);
                break;

            case Button.Previous:
                g.FillPolygon(iconBrush, new[] { new PointF(cx + 11, cy - 11), new PointF(cx + 11, cy + 11), new PointF(cx - 5, cy) });
                g.FillRectangle(iconBrush, cx - 11, cy - 11, 4, 22);
                break;
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

    private PointF ToBase(Point p) => new(p.X / ScaleFactor, p.Y / ScaleFactor);

    private Button? HitTestButton(Point p)
    {
        var b = ToBase(p);
        if (Contains(PrevRect, b)) return Button.Previous;
        if (Contains(PlayRect, b)) return Button.Play;
        if (Contains(NextRect, b)) return Button.Next;
        return null;
    }

    /// <summary>用外接圆做命中判断，使点击区域与圆形按钮一致。</summary>
    private static bool Contains(RectangleF r, PointF p)
    {
        var dx = p.X - (r.X + r.Width / 2);
        var dy = p.Y - (r.Y + r.Height / 2);
        return dx * dx + dy * dy <= r.Width * r.Width / 4;
    }

    /// <summary>列表模式下，鼠标位置对应的队列索引；不在条目行上时返回 -1。</summary>
    private int ListIndexAt(Point p)
    {
        var b = ToBase(p);
        if (!LcdRect.Contains(b)) return -1;
        var row = (int)Math.Floor((b.Y - LcdRect.Top - ListHeaderHeight) / ListRowHeight);
        return row < 0 ? -1 : _browser.IndexAtRow(row);
    }

    private bool IsEnabled(Button b) => b switch
    {
        Button.Previous => _view.CanPrevious,
        Button.Play => _view.CanTogglePause,
        _ => _view.CanNext,
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

        if (!LcdRect.Contains(ToBase(e.Location))) return;

        if (!_listMode) OpenList();
        else if (ListIndexAt(e.Location) is var index and >= 0) { _browser.SetCursor(index); Invalidate(); }
        else if (ToBase(e.Location).Y < LcdRect.Top + ListHeaderHeight) CloseList(); // 点击标题栏：返回播放显示
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
                default: NextClicked?.Invoke(); break;
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
