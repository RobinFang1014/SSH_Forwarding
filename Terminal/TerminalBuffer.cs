using System.Windows.Media;

namespace BIOSForwardManager.Terminal;

/// <summary>
/// 終端機螢幕 buffer：2D 格子 + 游標 + 捲動區 + SGR 狀態 + alternate screen + scrollback。
/// 純邏輯（僅用 System.Windows.Media.Color 當顏色值），不依賴 WPF UI。
/// </summary>
public class TerminalBuffer
{
    public static readonly Color DefaultFg = Color.FromRgb(0xDC, 0xDC, 0xDC);
    public static readonly Color DefaultBg = Color.FromRgb(0x1E, 0x1E, 0x1E);

    // 目前生效的預設色（可透過 SetDefaultColors 即時變更，live 更新所有用預設色的格子）
    private Color _fgDefault = DefaultFg, _bgDefault = DefaultBg;
    public Color FgDefault => _fgDefault;
    public Color BgDefault => _bgDefault;

    private int _maxScrollback = 5000;

    private int _rows, _cols;
    private TerminalCell[] _main;
    private TerminalCell[] _alt;
    private bool[] _mainWrapped; // 每列是否為「換行列」（內容續到下一列），供 reflow 用
    private bool _inAltScreen;

    private int _cursorRow, _cursorCol;
    private int _savedRow, _savedCol;
    private bool _savedCursorValid;
    private bool _pendingWrap;

    private int _scrollTop, _scrollBottom;
    private bool _autoWrap = true;
    private bool _cursorVisible = true;

    // 目前 SGR 狀態
    private Color _fg = DefaultFg, _bg = DefaultBg;
    private bool _bold, _italic, _underline, _inverse;

    private readonly List<TerminalCell[]> _scrollback = new(); // 存 cells（保留顏色），與主螢幕同結構
    private readonly List<bool> _scrollbackWrapped = new(); // 與 _scrollback 平行：每列是否為換行列

    /// <summary>任何 buffer 變更後觸發（供 control 做 InvalidateVisual）。</summary>
    public event Action? Changed;

    public int Rows => _rows;
    public int Cols => _cols;
    public int CursorRow => _cursorRow;
    public int CursorCol => _cursorCol;
    public bool CursorVisible => _cursorVisible;
    public bool InAltScreen => _inAltScreen;
    public IReadOnlyList<TerminalCell[]> Scrollback => _scrollback;

    /// <summary>取得 scrollback 某列某欄的 cell（供繪製，保留顏色）。</summary>
    public TerminalCell GetScrollbackCell(int line, int col)
    {
        if (line < 0 || line >= _scrollback.Count) return TerminalCell.Default(_fgDefault, _bgDefault);
        var cells = _scrollback[line];
        if (col < 0 || col >= cells.Length) return TerminalCell.Default(_fgDefault, _bgDefault);
        return cells[col];
    }

    public TerminalBuffer(int rows = 24, int cols = 80)
    {
        _rows = Math.Max(1, rows);
        _cols = Math.Max(1, cols);
        _main = new TerminalCell[_rows * _cols];
        _alt = new TerminalCell[_rows * _cols];
        _mainWrapped = new bool[_rows];
        ClearAll(_main);
        ClearAll(_alt);
        _scrollTop = 0;
        _scrollBottom = _rows - 1;
    }

    private TerminalCell[] Active => _inAltScreen ? _alt : _main;

    private TerminalCell ErasedCell => new(' ', _fg, _bg);

    private void RaiseChanged() => Changed?.Invoke();

    private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);

    private void ClearAll(TerminalCell[] buf)
    {
        for (int i = 0; i < buf.Length; i++) buf[i] = TerminalCell.Default(_fgDefault, _bgDefault);
    }

    /// <summary>取得指定位置的格子（供渲染/選取用）。</summary>
    public TerminalCell GetCell(int row, int col)
    {
        if (row < 0 || row >= _rows || col < 0 || col >= _cols)
            return TerminalCell.Default(_fgDefault, _bgDefault);
        return Active[row * _cols + col];
    }

    // ===== 寫入與游標 =====

    public void PutChar(char c)
    {
        if (_pendingWrap)
        {
            _pendingWrap = false;
            _cursorCol = 0;
            LineFeedInternal();
        }
        var buf = Active;
        int idx = _cursorRow * _cols + _cursorCol;
        buf[idx] = new TerminalCell(c, _fg, _bg, _bold, _italic, _underline, _inverse);
        if (_cursorCol == _cols - 1)
        {
            if (_autoWrap)
            {
                _pendingWrap = true;
                if (!_inAltScreen) _mainWrapped[_cursorRow] = true; // 此列被換行填滿 → 續到下一列
            }
        }
        else
        {
            _cursorCol++;
        }
        RaiseChanged();
    }

    public void CarriageReturn()
    {
        if (_pendingWrap && !_inAltScreen) _mainWrapped[_cursorRow] = false; // CR 取消換行 → 此列非換行列
        _cursorCol = 0;
        _pendingWrap = false;
        RaiseChanged();
    }

    public void NewLine()
    {
        // VT 標準：LF(IND) 在延遲換行狀態時先回到欄首再下移。
        if (_pendingWrap) _cursorCol = 0;
        // 注意：LF 不清除換行旗標（同 xterm）。滿列後的 LF 可能是「換行續行」
        // （readline 輸入列換行時會送 LF 把游標推到下一行），只有 CR 才代表取消換行（段落界線）。
        _pendingWrap = false;
        LineFeedInternal();
        RaiseChanged();
    }

    private void LineFeedInternal()
    {
        if (_cursorRow == _scrollBottom) ScrollUp(1);
        else if (_cursorRow < _rows - 1) _cursorRow++;
    }

    public void Backspace()
    {
        if (_cursorCol > 0) _cursorCol--;
        _pendingWrap = false;
        RaiseChanged();
    }

    public void Tab()
    {
        _pendingWrap = false;
        int next = ((_cursorCol / 8) + 1) * 8;
        _cursorCol = Math.Min(next, _cols - 1);
        RaiseChanged();
    }

    // ===== 游標移動 =====

    /// <summary>CSI H / f：絕對定位（1-based）。</summary>
    public void CursorPosition(int row, int col)
    {
        _cursorRow = Clamp(row - 1, 0, _rows - 1);
        _cursorCol = Clamp(col - 1, 0, _cols - 1);
        _pendingWrap = false;
        RaiseChanged();
    }

    /// <summary>CSI A/B/C/D：相對移動。</summary>
    public void CursorMove(int dRow, int dCol)
    {
        _cursorRow = Clamp(_cursorRow + dRow, 0, _rows - 1);
        _cursorCol = Clamp(_cursorCol + dCol, 0, _cols - 1);
        _pendingWrap = false;
        RaiseChanged();
    }

    /// <summary>CSI G / `：移到第 n 欄（1-based）。</summary>
    public void CursorToCol(int col)
    {
        _cursorCol = Clamp(col - 1, 0, _cols - 1);
        _pendingWrap = false;
        RaiseChanged();
    }

    /// <summary>CSI d：移到第 n 列（1-based）。</summary>
    public void CursorToRow(int row)
    {
        _cursorRow = Clamp(row - 1, 0, _rows - 1);
        RaiseChanged();
    }

    /// <summary>CSI E：下移 n 列並回欄首。</summary>
    public void CursorNextLine(int n)
    {
        _cursorRow = Clamp(_cursorRow + n, 0, _rows - 1);
        _cursorCol = 0;
        _pendingWrap = false;
        RaiseChanged();
    }

    /// <summary>CSI F：上移 n 列並回欄首。</summary>
    public void CursorPrevLine(int n)
    {
        _cursorRow = Clamp(_cursorRow - n, 0, _rows - 1);
        _cursorCol = 0;
        _pendingWrap = false;
        RaiseChanged();
    }

    /// <summary>CSI s：存游標。</summary>
    public void SaveCursor()
    {
        _savedRow = _cursorRow;
        _savedCol = _cursorCol;
        _savedCursorValid = true;
    }

    /// <summary>CSI u：還原游標。</summary>
    public void RestoreCursor()
    {
        if (_savedCursorValid)
        {
            _cursorRow = _savedRow;
            _cursorCol = _savedCol;
            _pendingWrap = false;
            RaiseChanged();
        }
    }

    // ===== 清除 =====

    /// <summary>CSI J：0=游標→尾、1=頭→游標、2=全清、3=全清+scrollback。</summary>
    public void EraseDisplay(int mode)
    {
        var buf = Active;
        switch (mode)
        {
            case 0:
                for (int c = _cursorCol; c < _cols; c++) buf[_cursorRow * _cols + c] = ErasedCell;
                for (int r = _cursorRow + 1; r < _rows; r++) ClearRow(r);
                break;
            case 1:
                for (int r = 0; r < _cursorRow; r++) ClearRow(r);
                for (int c = 0; c <= _cursorCol; c++) buf[_cursorRow * _cols + c] = ErasedCell;
                break;
            case 2:
            case 3:
                for (int r = 0; r < _rows; r++) ClearRow(r);
                if (mode == 3) { _scrollback.Clear(); _scrollbackWrapped.Clear(); }
                break;
        }
        RaiseChanged();
    }

    /// <summary>CSI K：0=游標→行尾、1=行首→游標、2=整行。</summary>
    public void EraseLine(int mode)
    {
        var buf = Active;
        int baseIdx = _cursorRow * _cols;
        switch (mode)
        {
            case 0:
                for (int c = _cursorCol; c < _cols; c++) buf[baseIdx + c] = ErasedCell;
                break;
            case 1:
                for (int c = 0; c <= _cursorCol; c++) buf[baseIdx + c] = ErasedCell;
                break;
            case 2:
                for (int c = 0; c < _cols; c++) buf[baseIdx + c] = ErasedCell;
                break;
        }
        RaiseChanged();
    }

    private void ClearRow(int row)
    {
        var buf = Active;
        int baseIdx = row * _cols;
        for (int c = 0; c < _cols; c++) buf[baseIdx + c] = ErasedCell;
    }

    // ===== 插入 / 刪除 =====

    /// <summary>CSI L：游標列插入 n 空白列（捲動區內）。</summary>
    public void InsertLine(int n)
    {
        if (_cursorRow < _scrollTop || _cursorRow > _scrollBottom) return;
        n = Math.Min(n, _scrollBottom - _cursorRow + 1);
        if (n <= 0) return;
        var buf = Active;
        Array.Copy(buf, _cursorRow * _cols, buf, (_cursorRow + n) * _cols, (_scrollBottom - _cursorRow - n + 1) * _cols);
        for (int r = _cursorRow; r < _cursorRow + n; r++) ClearRow(r);
        RaiseChanged();
    }

    /// <summary>CSI M：游標列刪除 n 列（上方內容下移）。</summary>
    public void DeleteLine(int n)
    {
        if (_cursorRow < _scrollTop || _cursorRow > _scrollBottom) return;
        n = Math.Min(n, _scrollBottom - _cursorRow + 1);
        if (n <= 0) return;
        var buf = Active;
        Array.Copy(buf, (_cursorRow + n) * _cols, buf, _cursorRow * _cols, (_scrollBottom - _cursorRow - n + 1) * _cols);
        for (int r = _scrollBottom - n + 1; r <= _scrollBottom; r++) ClearRow(r);
        RaiseChanged();
    }

    /// <summary>CSI @：游標處插入 n 空白字元（右移）。</summary>
    public void InsertChars(int n)
    {
        var buf = Active;
        int baseIdx = _cursorRow * _cols;
        n = Math.Min(n, _cols - _cursorCol);
        if (n <= 0) return;
        Array.Copy(buf, baseIdx + _cursorCol, buf, baseIdx + _cursorCol + n, _cols - _cursorCol - n);
        for (int c = _cursorCol; c < _cursorCol + n; c++) buf[baseIdx + c] = ErasedCell;
        RaiseChanged();
    }

    /// <summary>CSI P / X：游標處刪除 n 字元（左移補白）。</summary>
    public void DeleteChars(int n)
    {
        var buf = Active;
        int baseIdx = _cursorRow * _cols;
        n = Math.Min(n, _cols - _cursorCol);
        if (n <= 0) return;
        Array.Copy(buf, baseIdx + _cursorCol + n, buf, baseIdx + _cursorCol, _cols - _cursorCol - n);
        for (int c = _cols - n; c < _cols; c++) buf[baseIdx + c] = ErasedCell;
        RaiseChanged();
    }

    // ===== 捲動 =====

    /// <summary>CSI r：設定捲動區（1-based，含端點）。</summary>
    public void SetScrollRegion(int top, int bottom)
    {
        if (top < 1) top = 1;
        if (bottom > _rows) bottom = _rows;
        if (top >= bottom) { _scrollTop = 0; _scrollBottom = _rows - 1; }
        else { _scrollTop = top - 1; _scrollBottom = bottom - 1; }
        _cursorRow = 0;
        _cursorCol = 0;
        _pendingWrap = false;
        RaiseChanged();
    }

    /// <summary>捲動區內容上移 n 行（主螢幕全區時捲出列進 scrollback）。</summary>
    public void ScrollUp(int n)
    {
        var buf = Active;
        bool toScrollback = !_inAltScreen && _scrollTop == 0 && _scrollBottom == _rows - 1;
        n = Math.Min(n, _scrollBottom - _scrollTop + 1);
        if (n <= 0) return;
        if (toScrollback)
        {
            for (int i = 0; i < n; i++)
            {
                int baseIdx = (_scrollTop + i) * _cols;
                var cells = new TerminalCell[_cols];
                Array.Copy(buf, baseIdx, cells, 0, _cols);
                _scrollback.Add(cells);
                _scrollbackWrapped.Add(_mainWrapped[_scrollTop + i]);
            }
            while (_scrollback.Count > _maxScrollback)
            {
                _scrollback.RemoveAt(0);
                _scrollbackWrapped.RemoveAt(0);
            }
        }
        Array.Copy(buf, (_scrollTop + n) * _cols, buf, _scrollTop * _cols, (_scrollBottom - _scrollTop - n + 1) * _cols);
        for (int r = _scrollBottom - n + 1; r <= _scrollBottom; r++) ClearRow(r);
        // 主螢幕：換行旗標隨列上移，底端補 false
        if (!_inAltScreen)
        {
            for (int r = _scrollTop; r <= _scrollBottom - n; r++)
                _mainWrapped[r] = _mainWrapped[r + n];
            for (int r = _scrollBottom - n + 1; r <= _scrollBottom; r++)
                _mainWrapped[r] = false;
        }
        RaiseChanged();
    }

    /// <summary>捲動區內容下移 n 行（頂端補白）。</summary>
    public void ScrollDown(int n)
    {
        var buf = Active;
        n = Math.Min(n, _scrollBottom - _scrollTop + 1);
        if (n <= 0) return;
        Array.Copy(buf, _scrollTop * _cols, buf, (_scrollTop + n) * _cols, (_scrollBottom - _scrollTop - n + 1) * _cols);
        for (int r = _scrollTop; r < _scrollTop + n; r++) ClearRow(r);
        RaiseChanged();
    }

    // ===== 模式 =====

    /// <summary>CSI h / l：7=autowrap、25=游標可見、47/1047/1049=alt screen、1048=存還原游標。</summary>
    public void SetMode(int mode, bool on)
    {
        switch (mode)
        {
            case 7: _autoWrap = on; break;
            case 25: _cursorVisible = on; break;
            case 47:
            case 1047:
                if (on && !_inAltScreen) EnterAltScreen(clear: true);
                else if (!on && _inAltScreen) LeaveAltScreen(restoreCursor: false);
                break;
            case 1048:
                if (on) SaveCursor(); else RestoreCursor();
                break;
            case 1049:
                if (on && !_inAltScreen) EnterAltScreen(clear: true);
                else if (!on && _inAltScreen) LeaveAltScreen(restoreCursor: true);
                break;
        }
        RaiseChanged();
    }

    private void EnterAltScreen(bool clear)
    {
        _savedRow = _cursorRow;
        _savedCol = _cursorCol;
        _savedCursorValid = true;
        _inAltScreen = true;
        if (clear)
        {
            ClearAll(_alt);
            _cursorRow = 0;
            _cursorCol = 0;
            _pendingWrap = false;
            _scrollTop = 0;
            _scrollBottom = _rows - 1;
        }
    }

    private void LeaveAltScreen(bool restoreCursor)
    {
        _inAltScreen = false;
        if (restoreCursor && _savedCursorValid)
        {
            _cursorRow = _savedRow;
            _cursorCol = _savedCol;
        }
        _pendingWrap = false;
        _scrollTop = 0;
        _scrollBottom = _rows - 1;
    }

    // ===== SGR 顏色 =====

    /// <summary>CSI m：解析 SGR params 並更新目前顏色/屬性狀態。</summary>
    public void ApplySgr(int[] p)
    {
        if (p.Length == 0) p = new[] { 0 };
        for (int i = 0; i < p.Length; i++)
        {
            int code = p[i];
            switch (code)
            {
                case 0: _fg = _fgDefault; _bg = _bgDefault; _bold = _italic = _underline = _inverse = false; break;
                case 1: _bold = true; break;
                case 3: _italic = true; break;
                case 4: _underline = true; break;
                case 7: _inverse = true; break;
                case 21:
                case 22: _bold = false; break;
                case 23: _italic = false; break;
                case 24: _underline = false; break;
                case 27: _inverse = false; break;
                case >= 30 and <= 37: _fg = Palette16[code - 30]; break;
                case 38:
                    if (i + 2 < p.Length && p[i + 1] == 5) { _fg = ColorFrom256(p[i + 2]); i += 2; }
                    else if (i + 4 < p.Length && p[i + 1] == 2) { _fg = Color.FromRgb((byte)p[i + 2], (byte)p[i + 3], (byte)p[i + 4]); i += 4; }
                    break;
                case 39: _fg = _fgDefault; break;
                case >= 40 and <= 47: _bg = Palette16[code - 40]; break;
                case 48:
                    if (i + 2 < p.Length && p[i + 1] == 5) { _bg = ColorFrom256(p[i + 2]); i += 2; }
                    else if (i + 4 < p.Length && p[i + 1] == 2) { _bg = Color.FromRgb((byte)p[i + 2], (byte)p[i + 3], (byte)p[i + 4]); i += 4; }
                    break;
                case 49: _bg = _bgDefault; break;
                case >= 90 and <= 97: _fg = Palette16[code - 90 + 8]; break;
                case >= 100 and <= 107: _bg = Palette16[code - 100 + 8]; break;
            }
        }
        RaiseChanged();
    }

    // ===== 重置 / 調整大小 =====

    /// <summary>RIS：完整重置。</summary>
    public void Reset()
    {
        ClearAll(_main);
        ClearAll(_alt);
        _inAltScreen = false;
        _cursorRow = _cursorCol = 0;
        _savedRow = _savedCol = 0;
        _savedCursorValid = false;
        _pendingWrap = false;
        _scrollTop = 0;
        _scrollBottom = _rows - 1;
        _autoWrap = true;
        _cursorVisible = true;
        _fg = _fgDefault;
        _bg = _bgDefault;
        _bold = _italic = _underline = _inverse = false;
        _scrollback.Clear();
        _scrollbackWrapped.Clear();
        Array.Clear(_mainWrapped, 0, _mainWrapped.Length);
        RaiseChanged();
    }

    /// <summary>
    /// 即時變更預設前景/背景色：remap 所有用舊預設色的格子（main+alt）與目前 SGR 狀態。
    /// 明確設定的顏色（palette/256/truecolor）不受影響。
    /// </summary>
    public void SetDefaultColors(Color fg, Color bg)
    {
        Color oldFg = _fgDefault, oldBg = _bgDefault;
        if (fg == oldFg && bg == oldBg) return;
        for (int i = 0; i < _main.Length; i++) _main[i] = RemapCell(_main[i], oldFg, oldBg, fg, bg);
        for (int i = 0; i < _alt.Length; i++) _alt[i] = RemapCell(_alt[i], oldFg, oldBg, fg, bg);
        if (_fg == oldFg) _fg = fg;
        if (_bg == oldBg) _bg = bg;
        _fgDefault = fg;
        _bgDefault = bg;
        RaiseChanged();
    }

    private static TerminalCell RemapCell(TerminalCell cell, Color oldFg, Color oldBg, Color newFg, Color newBg)
    {
        Color fg = cell.Fg == oldFg ? newFg : cell.Fg;
        Color bg = cell.Bg == oldBg ? newBg : cell.Bg;
        if (fg == cell.Fg && bg == cell.Bg) return cell;
        return cell.WithStyle(fg, bg, cell.Bold, cell.Italic, cell.Underline, cell.Inverse);
    }

    /// <summary>設定 scrollback 上限列數（立即裁掉多餘部分）。</summary>
    public void SetScrollback(int lines)
    {
        _maxScrollback = Math.Max(100, lines);
        while (_scrollback.Count > _maxScrollback)
        {
            _scrollback.RemoveAt(0);
            _scrollbackWrapped.RemoveAt(0);
        }
    }

    /// <summary>調整大小（保留重疊內容）。</summary>
    public void Resize(int rows, int cols)
    {
        rows = Math.Max(1, rows);
        cols = Math.Max(1, cols);
        if (rows == _rows && cols == _cols) return;

        bool colsChanged = cols != _cols;
        int oldRows = _rows, oldCols = _cols;

        var newAlt = new TerminalCell[rows * cols];
        ClearAll(newAlt);

        if (colsChanged && !_inAltScreen)
        {
            // 統一 reflow：scrollback + 主螢幕當一個連續串流（同 xterm/Windows Terminal），
            // 跨 scrollback/主螢幕邊界的換行段落也能正確合併
            ReflowAll(rows, cols);
        }
        else
        {
            // 只改列數、或在 alt screen（less/vim 等會自己重繪）：保留重疊內容
            var newMain = new TerminalCell[rows * cols];
            var newMainWrapped = new bool[rows];
            ClearAll(newMain);
            int copyRows = Math.Min(rows, oldRows);
            int copyCols = Math.Min(cols, oldCols);
            for (int r = 0; r < copyRows; r++)
                Array.Copy(_main, r * oldCols, newMain, r * cols, copyCols);
            Array.Copy(_mainWrapped, newMainWrapped, copyRows);
            _main = newMain;
            _mainWrapped = newMainWrapped;
            _rows = rows;
            _cols = cols;
        }
        // alt screen 一律重疊複製（由該程式自己依 SIGWINCH 重繪）
        {
            int copyRows = Math.Min(rows, oldRows);
            int copyCols = Math.Min(cols, oldCols);
            for (int r = 0; r < copyRows; r++)
                Array.Copy(_alt, r * oldCols, newAlt, r * cols, copyCols);
        }
        _alt = newAlt;
        _scrollTop = 0;
        _scrollBottom = rows - 1;
        _cursorRow = Clamp(_cursorRow, 0, rows - 1);
        _cursorCol = Clamp(_cursorCol, 0, cols - 1);
        _pendingWrap = false;
        RaiseChanged();
    }

    /// <summary>
    /// 統一 reflow：把 scrollback + 主螢幕當成「一個連續串流」（同 xterm/Windows Terminal），
    /// 依新寬度重新換行。這樣跨 scrollback/主螢幕邊界的換行段落也能正確合併
    /// （分開 reflow 時段落會被邊界截斷、永久無法合併）。
    /// scrollback 與主螢幕都存 cells，reflow 後每格顏色/樣式完整保留。
    /// 段落判定用寫入時記錄的換行旗標（可重複 reflow）。
    /// </summary>
    private void ReflowAll(int newRows, int newCols)
    {
        int oldRows = _rows, oldCols = _cols;
        int oldSbCount = _scrollback.Count;

        // 建立連續串流：scrollback（cells）+ 主螢幕（cells）
        var lines = new List<TerminalCell[]>(oldSbCount + oldRows);
        var wrapped = new List<bool>(oldSbCount + oldRows);
        for (int k = 0; k < oldSbCount; k++)
        {
            lines.Add(_scrollback[k]);
            wrapped.Add(k < _scrollbackWrapped.Count && _scrollbackWrapped[k]);
        }
        for (int r = 0; r < oldRows; r++)
        {
            var cells = new TerminalCell[oldCols];
            Array.Copy(_main, r * oldCols, cells, 0, oldCols);
            lines.Add(cells);
            wrapped.Add(r < _mainWrapped.Length && _mainWrapped[r]);
        }

        int cursorStreamLine = oldSbCount + _cursorRow;

        // 逐段落重新換行（全 cells，保留顏色）
        var result = new List<TerminalCell[]>(lines.Count);
        var resultWrapped = new List<bool>(lines.Count);
        int cursorNewStreamLine = -1, cursorNewCol = 0;
        int i = 0;
        while (i < lines.Count)
        {
            // 收集一個段落：目前列 + 之後被標記為換行的列（用旗標，非空白啟發式）
            int paraStart = i;
            bool w = wrapped[i];
            i++;
            while (w && i < lines.Count)
            {
                w = wrapped[i];
                i++;
            }
            int paraCount = i - paraStart;
            int paraResultStart = result.Count;

            // 游標在段內的字元偏移（若游標在此段落）
            int cursorOffset = -1;
            int offsetSoFar = 0;
            for (int p = 0; p < paraCount; p++)
            {
                if (paraStart + p == cursorStreamLine) cursorOffset = offsetSoFar + _cursorCol;
                offsetSoFar += oldCols;
            }

            // 拼接段落所有 cells，去掉尾端空白格
            var cellList = new List<TerminalCell>(oldCols * paraCount);
            for (int p = 0; p < paraCount; p++)
                cellList.AddRange(lines[paraStart + p]);
            while (cellList.Count > 0 && cellList[^1].Ch == ' ') cellList.RemoveAt(cellList.Count - 1);
            if (cursorOffset > cellList.Count) cursorOffset = cellList.Count;

            if (cellList.Count == 0)
            {
                var blank = new TerminalCell[newCols];
                for (int c = 0; c < newCols; c++) blank[c] = TerminalCell.Default(_fgDefault, _bgDefault);
                result.Add(blank);
                resultWrapped.Add(false);
            }
            else
            {
                for (int p = 0; p < cellList.Count; p += newCols)
                {
                    int len = Math.Min(newCols, cellList.Count - p);
                    var newCells = new TerminalCell[newCols];
                    for (int c = 0; c < newCols; c++)
                        newCells[c] = c < len ? cellList[p + c] : TerminalCell.Default(_fgDefault, _bgDefault);
                    result.Add(newCells);
                    resultWrapped.Add(p + len < cellList.Count);
                }
            }

            if (cursorOffset >= 0)
            {
                cursorNewStreamLine = paraResultStart + cursorOffset / newCols;
                cursorNewCol = cursorOffset % newCols;
            }
        }

        // 切分：最底 newRows 列 → 主螢幕，其餘 → scrollback
        int total = result.Count;
        int newSbCount = Math.Max(0, total - newRows);

        _scrollback.Clear();
        _scrollbackWrapped.Clear();
        for (int k = 0; k < newSbCount; k++)
        {
            _scrollback.Add(result[k]);
            _scrollbackWrapped.Add(resultWrapped[k]);
        }
        while (_scrollback.Count > _maxScrollback)
        {
            _scrollback.RemoveAt(0);
            _scrollbackWrapped.RemoveAt(0);
        }

        var newMain = new TerminalCell[newRows * newCols];
        var newMainWrapped = new bool[newRows];
        ClearAll(newMain);
        for (int r = 0; r < newRows; r++)
        {
            int k = newSbCount + r;
            if (k >= total) break;
            Array.Copy(result[k], 0, newMain, r * newCols, newCols);
            newMainWrapped[r] = resultWrapped[k];
        }

        _main = newMain;
        _mainWrapped = newMainWrapped;
        _rows = newRows;
        _cols = newCols;

        if (cursorNewStreamLine >= 0)
        {
            _cursorRow = Clamp(cursorNewStreamLine - newSbCount, 0, newRows - 1);
            _cursorCol = Clamp(cursorNewCol, 0, newCols - 1);
        }
        _pendingWrap = false;
    }

    // ===== 顏色 palette =====

    private static readonly Color[] Palette16 =
    {
        Color.FromRgb(0x00, 0x00, 0x00), // 0 black
        Color.FromRgb(0xCD, 0x31, 0x31), // 1 red
        Color.FromRgb(0x0D, 0xBC, 0x79), // 2 green
        Color.FromRgb(0xE5, 0xE5, 0x10), // 3 yellow
        Color.FromRgb(0x24, 0x72, 0xC8), // 4 blue
        Color.FromRgb(0xBC, 0x3F, 0xBC), // 5 magenta
        Color.FromRgb(0x11, 0xA8, 0xCD), // 6 cyan
        Color.FromRgb(0xE5, 0xE5, 0xE5), // 7 white
        Color.FromRgb(0x66, 0x66, 0x66), // 8 bright black
        Color.FromRgb(0xFC, 0x54, 0x54), // 9 bright red
        Color.FromRgb(0x23, 0xD1, 0x8B), // 10 bright green
        Color.FromRgb(0xF5, 0xF5, 0x43), // 11 bright yellow
        Color.FromRgb(0x3B, 0x8E, 0xEA), // 12 bright blue
        Color.FromRgb(0xD6, 0x70, 0xD6), // 13 bright magenta
        Color.FromRgb(0x29, 0xB8, 0xDB), // 14 bright cyan
        Color.FromRgb(0xFF, 0xFF, 0xFF), // 15 bright white
    };

    private static Color ColorFrom256(int n)
    {
        if (n < 16) return Palette16[n];
        if (n < 232)
        {
            int idx = n - 16;
            int r = (idx / 36) * 43 + 5;
            int g = ((idx / 6) % 6) * 43 + 5;
            int b = (idx % 6) * 43 + 5;
            return Color.FromRgb((byte)r, (byte)g, (byte)b);
        }
        int gray = (n - 232) * 10 + 8;
        return Color.FromRgb((byte)gray, (byte)gray, (byte)gray);
    }
}
