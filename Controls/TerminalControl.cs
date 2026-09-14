using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BIOSForwardManager.Models;
using BIOSForwardManager.Terminal;

namespace BIOSForwardManager.Controls;

/// <summary>
/// 自訂 WPF 終端機 control：grid-based 渲染 + 鍵盤輸入 + 滑鼠選取 + scrollback 捲動。
/// 資料流：遠端輸出 → <see cref="Write"/> → Parser → Buffer → InvalidateVisual → OnRender；
/// 鍵盤 → <see cref="Input"/> 事件（escape sequence 字串）→ 由宿主寫入 shell。
/// </summary>
public class TerminalControl : System.Windows.Controls.Control
{
    private readonly TerminalBuffer _buffer;
    private readonly TerminalParser _parser;
    private Typeface _normalFace;
    private Typeface _boldFace;
    private readonly DispatcherTimer _cursorTimer;
    private Brush _bgBrush = Freeze(new SolidColorBrush(TerminalBuffer.DefaultBg));
    private Color _cursorColor = TerminalBuffer.DefaultFg;

    private double _cellWidth, _cellHeight;
    private bool _cursorBlinkVisible = true;

    // 選取（僅在底部/live 時有效）
    private bool _hasSelection;
    private (int row, int col) _selAnchor, _selHead;

    // scrollback 捲動（0 = 底部/live）
    private int _scrollOffset;

    public TerminalBuffer Buffer => _buffer;

    /// <summary>使用者輸入（escape sequence 字串）。</summary>
    public event Action<string>? Input;

    /// <summary>buffer 尺寸變更（rows, cols），供宿主送 SIGWINCH。</summary>
    public event Action<int, int>? ShellSizeChanged;

    /// <summary>捲動狀態變更（offset 或 scrollback 數量變化），供宿主同步 scroll bar。</summary>
    public event Action? ScrollStateChanged;

    /// <summary>目前捲動偏移（0 = 底部/live）。</summary>
    public int ScrollOffset => _scrollOffset;
    /// <summary>scrollback 總列數。</summary>
    public int ScrollbackCount => _buffer.Scrollback.Count;
    /// <summary>可見列數（尚未量測時為 0）。</summary>
    public int VisibleRows => _cellHeight > 0 ? Math.Max(1, (int)(ActualHeight / _cellHeight)) : 0;

    /// <summary>設定捲動偏移（0 = 底部/live），自動 clamp 到有效範圍。</summary>
    public void SetScrollOffset(int offset)
    {
        int clamped = Math.Clamp(offset, 0, _buffer.Scrollback.Count);
        if (clamped == _scrollOffset) return;
        _scrollOffset = clamped;
        InvalidateVisual();
        ScrollStateChanged?.Invoke();
    }

    public TerminalControl()
    {
        FontFamily = new FontFamily("Consolas, Courier New");
        FontSize = 14;
        Focusable = true;
        ClipToBounds = true;

        _buffer = new TerminalBuffer(24, 80);
        _parser = new TerminalParser(_buffer);
        _normalFace = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        _boldFace = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        _buffer.Changed += OnBufferChanged;

        _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _cursorTimer.Tick += (_, _) =>
        {
            _cursorBlinkVisible = !_cursorBlinkVisible;
            InvalidateVisual();
        };
        _cursorTimer.Start();

        Loaded += (_, _) =>
        {
            MeasureCell();
            UpdateBufferSize();
            Focus();
        };
        SizeChanged += (_, _) =>
        {
            MeasureCell();
            UpdateBufferSize();
            ScrollStateChanged?.Invoke(); // 可見列數變化
        };
    }

    private void OnBufferChanged()
    {
        _cursorBlinkVisible = true; // 新輸出時重設閃爍相位
        InvalidateVisual();
        ScrollStateChanged?.Invoke(); // scrollback 數量可能變化
    }

    private double Dpi
    {
        get
        {
            try { return VisualTreeHelper.GetDpi(this).PixelsPerDip; }
            catch { return 1.0; }
        }
    }

    private void MeasureCell()
    {
        var ft = new FormattedText("M", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            _normalFace, FontSize, Brushes.White, Dpi);
        _cellWidth = ft.WidthIncludingTrailingWhitespace;
        _cellHeight = ft.Height;
    }

    private void UpdateBufferSize()
    {
        if (_cellWidth <= 0 || _cellHeight <= 0) return;
        if (ActualWidth <= 0 || ActualHeight <= 0) return; // 尚未量測（視窗未載入），避免誤縮成 1x1
        int cols = Math.Max(1, (int)(ActualWidth / _cellWidth));
        int rows = Math.Max(1, (int)(ActualHeight / _cellHeight));
        if (cols != _buffer.Cols || rows != _buffer.Rows)
        {
            _buffer.Resize(rows, cols);
            ShellSizeChanged?.Invoke(rows, cols);
        }
    }

    /// <summary>遠端輸出進入（須在 UI thread 呼叫）。</summary>
    public void Write(byte[] data, int offset, int count)
    {
        _parser.Feed(data, offset, count);
    }

    /// <summary>取得目前選取的文字（無選取回空字串）。</summary>
    public string GetSelectionText()
    {
        if (!_hasSelection) return string.Empty;
        (int r1, int c1) = _selAnchor;
        (int r2, int c2) = _selHead;
        if (r1 > r2 || (r1 == r2 && c1 > c2))
            ((r1, c1), (r2, c2)) = ((r2, c2), (r1, c1));
        var sb = new StringBuilder();
        for (int r = r1; r <= r2; r++)
        {
            int cStart = (r == r1) ? c1 : 0;
            int cEnd = (r == r2) ? c2 : _buffer.Cols - 1;
            for (int c = cStart; c <= cEnd; c++)
                sb.Append(_buffer.GetCell(r, c).Ch);
            if (r < r2) sb.Append('\n');
        }
        return sb.ToString();
    }

    public void ClearSelection()
    {
        _hasSelection = false;
        InvalidateVisual();
    }

    /// <summary>
    /// 套用終端機設定（字型/大小/粗體、顏色、scrollback、游標閃爍）。
    /// 須在 UI thread 呼叫。字型變更會重算 cell 尺寸並觸發 <see cref="ShellSizeChanged"/>（SIGWINCH）。
    /// </summary>
    public void ApplySettings(TerminalSettings s)
    {
        // 比例字型會破壞 grid 對齊（字元錯位、中間出現奇怪空白）→ 強制回退 Consolas
        if (!IsMonospaceFont(s.FontFamily, Dpi)) s.FontFamily = "Consolas";

        // 字型
        var family = new FontFamily(s.FontFamily);
        var baseWeight = s.Bold ? FontWeights.Bold : FontWeights.Normal;
        FontFamily = family;
        FontSize = s.FontSize;
        _normalFace = new Typeface(family, FontStyles.Normal, baseWeight, FontStretches.Normal);
        _boldFace = new Typeface(family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        // 顏色（live 更新 buffer 內所有用預設色的格子）
        var bg = ParseColor(s.Background, TerminalBuffer.DefaultBg);
        var fg = ParseColor(s.Foreground, TerminalBuffer.DefaultFg);
        _buffer.SetDefaultColors(fg, bg);
        _bgBrush = Freeze(new SolidColorBrush(bg));
        _cursorColor = ParseColor(s.CursorColor, fg);

        // scrollback
        _buffer.SetScrollback(s.ScrollbackLines);

        // 游標閃爍
        if (s.CursorBlink)
        {
            if (!_cursorTimer.IsEnabled) _cursorTimer.Start();
        }
        else
        {
            _cursorTimer.Stop();
            _cursorBlinkVisible = true;
        }

        // 重算 cell 尺寸 + buffer 大小（觸發 SIGWINCH）+ 重繪
        MeasureCell();
        UpdateBufferSize();
        InvalidateVisual();
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return fallback; }
    }

    /// <summary>
    /// 判斷是否為等寬字型（所有 ASCII 字元寬度一致）。
    /// 終端機是 grid-based 渲染（每字元固定一格），比例字型（微軟正黑體等）會導致字元錯位/奇怪空白。
    /// </summary>
    public static bool IsMonospaceFont(string familyName, double dpi = 1.0)
    {
        try
        {
            var tf = new Typeface(new FontFamily(familyName), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            double WidthOf(string s) => new FormattedText(s, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, tf, 20, Brushes.Black, dpi).WidthIncludingTrailingWhitespace;
            double wM = WidthOf("M");
            if (wM <= 0) return false;
            // 10 個同字元必須正好 10 倍寬（排除比例/連字）
            if (Math.Abs(WidthOf("MMMMMMMMMM") - wM * 10) > 0.5) return false;
            // 不同字元寬度必須一致
            foreach (var ch in new[] { 'm', 'W', 'i', 'l', '1', 'x', '@' })
                if (Math.Abs(WidthOf(ch.ToString()) - wM) > 0.5) return false;
            return true;
        }
        catch { return false; }
    }

    // ===== 渲染 =====

    protected override void OnRender(DrawingContext dc)
    {
        UpdateBufferSize();

        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(_bgBrush, null, new Rect(0, 0, w, h));

        int visibleRows = (int)(h / _cellHeight);
        int visibleCols = (int)(w / _cellWidth);
        if (visibleRows <= 0 || visibleCols <= 0) return;

        int scrollCount = _buffer.Scrollback.Count;
        int totalLines = scrollCount + _buffer.Rows;
        if (_scrollOffset > scrollCount) _scrollOffset = scrollCount;

        int firstLine = totalLines - visibleRows - _scrollOffset;
        if (firstLine < 0) firstLine = 0;

        for (int screenRow = 0; screenRow < visibleRows; screenRow++)
        {
            int lineIdx = firstLine + screenRow;
            if (lineIdx >= totalLines) break;
            double y = screenRow * _cellHeight;
            if (lineIdx < scrollCount)
                DrawScrollbackRow(dc, lineIdx, y, visibleCols);
            else
                DrawBufferRow(dc, lineIdx - scrollCount, y, visibleCols);
        }

        DrawSelection(dc, visibleRows, visibleCols);
        DrawCursor(dc, visibleRows, visibleCols);
    }

    private void DrawBufferRow(DrawingContext dc, int bufRow, double y, int visibleCols)
    {
        // 背景（合併連續同色）
        int col = 0;
        while (col < visibleCols)
        {
            var cell = _buffer.GetCell(bufRow, col);
            Color bg = cell.Inverse ? cell.Fg : cell.Bg;
            int start = col;
            col++;
            while (col < visibleCols)
            {
                var c2 = _buffer.GetCell(bufRow, col);
                Color bg2 = c2.Inverse ? c2.Fg : c2.Bg;
                if (bg2 != bg) break;
                col++;
            }
            if (bg != _buffer.BgDefault)
                dc.DrawRectangle(new SolidColorBrush(bg), null,
                    new Rect(start * _cellWidth, y, (col - start) * _cellWidth, _cellHeight));
        }

        // 文字（合併連續同 fg/bold）
        col = 0;
        while (col < visibleCols)
        {
            var cell = _buffer.GetCell(bufRow, col);
            Color fg = cell.Inverse ? cell.Bg : cell.Fg;
            bool bold = cell.Bold;
            int start = col;
            var sb = new StringBuilder();
            sb.Append(cell.Ch);
            col++;
            while (col < visibleCols)
            {
                var c2 = _buffer.GetCell(bufRow, col);
                Color fg2 = c2.Inverse ? c2.Bg : c2.Fg;
                if (fg2 != fg || c2.Bold != bold) break;
                sb.Append(c2.Ch);
                col++;
            }
            DrawTextRun(dc, sb.ToString(), start, y, fg, bold);
        }
    }

    /// <summary>繪製 scrollback 列（cells，保留顏色；邏輯同 DrawBufferRow，改讀 GetScrollbackCell）。</summary>
    private void DrawScrollbackRow(DrawingContext dc, int sbLine, double y, int visibleCols)
    {
        // 背景（合併連續同色）
        int col = 0;
        while (col < visibleCols)
        {
            var cell = _buffer.GetScrollbackCell(sbLine, col);
            Color bg = cell.Inverse ? cell.Fg : cell.Bg;
            int start = col;
            col++;
            while (col < visibleCols)
            {
                var c2 = _buffer.GetScrollbackCell(sbLine, col);
                Color bg2 = c2.Inverse ? c2.Fg : c2.Bg;
                if (bg2 != bg) break;
                col++;
            }
            if (bg != _buffer.BgDefault)
                dc.DrawRectangle(new SolidColorBrush(bg), null,
                    new Rect(start * _cellWidth, y, (col - start) * _cellWidth, _cellHeight));
        }

        // 文字（合併連續同 fg/bold）
        col = 0;
        while (col < visibleCols)
        {
            var cell = _buffer.GetScrollbackCell(sbLine, col);
            Color fg = cell.Inverse ? cell.Bg : cell.Fg;
            bool bold = cell.Bold;
            int start = col;
            var sb = new StringBuilder();
            sb.Append(cell.Ch);
            col++;
            while (col < visibleCols)
            {
                var c2 = _buffer.GetScrollbackCell(sbLine, col);
                Color fg2 = c2.Inverse ? c2.Bg : c2.Fg;
                if (fg2 != fg || c2.Bold != bold) break;
                sb.Append(c2.Ch);
                col++;
            }
            DrawTextRun(dc, sb.ToString(), start, y, fg, bold);
        }
    }

    private void DrawTextRun(DrawingContext dc, string text, int startCol, double y, Color fg, bool bold)
    {
        if (string.IsNullOrEmpty(text)) return;
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            bold ? _boldFace : _normalFace, FontSize, new SolidColorBrush(fg), Dpi);
        dc.DrawText(ft, new Point(startCol * _cellWidth, y));
    }

    private void DrawSelection(DrawingContext dc, int visibleRows, int visibleCols)
    {
        if (!_hasSelection || _scrollOffset != 0) return;
        (int r1, int c1) = _selAnchor;
        (int r2, int c2) = _selHead;
        if (r1 > r2 || (r1 == r2 && c1 > c2))
            ((r1, c1), (r2, c2)) = ((r2, c2), (r1, c1));
        var highlight = new SolidColorBrush(Color.FromArgb(0x66, 0x26, 0x4F, 0x78));
        for (int r = r1; r <= r2 && r < visibleRows; r++)
        {
            int cStart = (r == r1) ? c1 : 0;
            int cEnd = (r == r2) ? c2 : visibleCols - 1;
            if (cEnd >= visibleCols) cEnd = visibleCols - 1;
            if (cStart > cEnd) continue;
            dc.DrawRectangle(highlight, null,
                new Rect(cStart * _cellWidth, r * _cellHeight, (cEnd - cStart + 1) * _cellWidth, _cellHeight));
        }
    }

    private void DrawCursor(DrawingContext dc, int visibleRows, int visibleCols)
    {
        if (_scrollOffset != 0 || !_buffer.CursorVisible || !_cursorBlinkVisible) return;
        int cr = _buffer.CursorRow, cc = _buffer.CursorCol;
        if (cr >= visibleRows || cc >= visibleCols) return;
        var cell = _buffer.GetCell(cr, cc);
        var rect = new Rect(cc * _cellWidth, cr * _cellHeight, _cellWidth, _cellHeight);
        dc.DrawRectangle(new SolidColorBrush(_cursorColor), null, rect);
        if (cell.Ch != ' ')
            DrawTextRun(dc, cell.Ch.ToString(), cc, cr * _cellHeight, cell.Bg, cell.Bold);
    }

    // ===== 鍵盤 =====

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // 有選取時 Ctrl+C = 複製（不送 shell）
        if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0 && _hasSelection)
        {
            string text = GetSelectionText();
            if (text.Length > 0)
            {
                try { Clipboard.SetText(text); } catch { }
                e.Handled = true;
                return;
            }
        }

        // Ctrl+V = 貼上（把剪貼簿文字送進 shell）
        if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            try
            {
                string text = Clipboard.GetText();
                if (text.Length > 0)
                {
                    _scrollOffset = 0;
                    Input?.Invoke(text);
                    e.Handled = true;
                    return;
                }
            }
            catch { }
        }

        string? input = KeyToInput(e.Key, Keyboard.Modifiers);
        if (input != null)
        {
            _scrollOffset = 0; // 輸入時回到底部
            Input?.Invoke(input);
            e.Handled = true;
        }
    }

    private string? KeyToInput(Key key, ModifierKeys mods)
    {
        bool ctrl = mods.HasFlag(ModifierKeys.Control);

        if (ctrl)
        {
            switch (key)
            {
                case Key.C: return "\x03";
                case Key.D: return "\x04";
                case Key.A: return "\x01";
                case Key.E: return "\x05";
                case Key.K: return "\x0B";
                case Key.L: return "\x0C";
                case Key.U: return "\x15";
                case Key.Z: return "\x1A";
                case Key.Left: return "\x1b[1;5D";
                case Key.Right: return "\x1b[1;5C";
                case Key.Home: return "\x1b[1;5H";
                case Key.End: return "\x1b[1;5F";
                case Key.Insert: return "\x1b[2~";
                case Key.Delete: return "\x1b[3;5~";
                case Key.PageUp: return "\x1b[5~";
                case Key.PageDown: return "\x1b[6~";
                default: return null;
            }
        }

        switch (key)
        {
            case Key.Enter: return "\r";
            case Key.Back: return "\b";
            case Key.Tab: return "\t";
            case Key.Space: return " ";
            case Key.Escape: return "\x1b";
            case Key.Up: return "\x1b[A";
            case Key.Down: return "\x1b[B";
            case Key.Right: return "\x1b[C";
            case Key.Left: return "\x1b[D";
            case Key.Home: return "\x1b[H";
            case Key.End: return "\x1b[F";
            case Key.Insert: return "\x1b[2~";
            case Key.Delete: return "\x1b[3~";
            case Key.PageUp: return "\x1b[5~";
            case Key.PageDown: return "\x1b[6~";

            case Key.F1: return "\x1bOP";
            case Key.F2: return "\x1bOQ";
            case Key.F3: return "\x1bOR";
            case Key.F4: return "\x1bOS";
            case Key.F5: return "\x1b[15~";
            case Key.F6: return "\x1b[17~";
            case Key.F7: return "\x1b[18~";
            case Key.F8: return "\x1b[19~";
            case Key.F9: return "\x1b[20~";
            case Key.F10: return "\x1b[21~";
            case Key.F11: return "\x1b[23~";
            case Key.F12: return "\x1b[24~";

            default:
                // 一般按鍵（字母/數字/符號/NumPad）→ 用 OS 鍵盤佈局轉換成實際字元。
                // 手動映射會漏鍵且綁死 US 佈局（zh-tw/CEISA 下 " \ | 在不同物理鍵，
                // !@#$%^&*() 是 shift+數字），改用 ToUnicode 依目前佈局正確轉換。
                return GetCharFromKey(key, mods);
        }
    }

    // ===== 鍵盤佈局感知的字元轉換（P/Invoke ToUnicode）=====

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int ToUnicode(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
        System.Text.StringBuilder pwszBuff, int cchBuff, uint wFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType); // MAPVK_VK_TO_VSC = 0

    /// <summary>用目前 OS 鍵盤佈局把 Key+修飾鍵轉換成實際字元（處理 shift 組合與佈局差異）。</summary>
    private string? GetCharFromKey(Key key, ModifierKeys mods)
    {
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return null;
        uint scan = MapVirtualKey(vk, 0); // MAPVK_VK_TO_VSC

        byte[] keyState = new byte[256];
        // 修飾鍵「按住」狀態要用 0x80（high bit = key down），不是 0x01（transition bit）。
        // 用 0x01 的話 ToUnicode 不會套用 Shift，導致 !@#$%^&*() 等 shift 字元打不出。
        if (mods.HasFlag(ModifierKeys.Shift)) keyState[0x10] = 0x80;   // VK_SHIFT down
        if (mods.HasFlag(ModifierKeys.Control)) keyState[0x11] = 0x80; // VK_CONTROL down
        if (mods.HasFlag(ModifierKeys.Alt)) keyState[0x12] = 0x80;     // VK_MENU down
        keyState[0x90] = (byte)((GetKeyState(0x90) & 0x01) != 0 ? 1 : 0); // VK_NUMLOCK toggle

        var sb = new System.Text.StringBuilder(4);
        int result = ToUnicode(vk, scan, keyState, sb, 4, 0);
        if (result > 0 && sb.Length > 0)
        {
            string s = sb.ToString();
            // 排除控制字元（特殊鍵已在上層處理）；dead key (result<0) 回 null
            if (s.Length == 1 && char.IsControl(s[0])) return null;
            return s;
        }
        return null;
    }

    // ===== 滑鼠選取 / 捲動 =====

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (_scrollOffset != 0) return; // 只在底部/live 允許選取
        var (row, col) = PixelToCell(e.GetPosition(this));
        _selAnchor = (row, col);
        _selHead = (row, col);
        _hasSelection = false;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsMouseCaptured || _scrollOffset != 0) return;
        var (row, col) = PixelToCell(e.GetPosition(this));
        _selHead = (row, col);
        if (row != _selAnchor.row || col != _selAnchor.col)
            _hasSelection = true;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        Focus();
        // 右鍵 = 貼上（終端機慣例）
        try
        {
            string text = Clipboard.GetText();
            if (text.Length > 0)
            {
                _scrollOffset = 0;
                Input?.Invoke(text);
                e.Handled = true;
            }
        }
        catch { }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        int delta = e.Delta > 0 ? 3 : -3; // 往上捲 = 看更舊內容 = offset 增加
        _scrollOffset = Math.Clamp(_scrollOffset + delta, 0, _buffer.Scrollback.Count);
        InvalidateVisual();
        ScrollStateChanged?.Invoke();
        e.Handled = true;
    }

    private (int row, int col) PixelToCell(Point p)
    {
        int col = Math.Clamp((int)(p.X / _cellWidth), 0, _buffer.Cols - 1);
        int row = Math.Clamp((int)(p.Y / _cellHeight), 0, _buffer.Rows - 1);
        return (row, col);
    }

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }
}
