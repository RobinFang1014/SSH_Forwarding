using System.Text;

namespace BIOSForwardManager.Terminal;

/// <summary>
/// ANSI/VT 終端機序列解析器（狀態機）。餵入 byte 流（增量 UTF-8 解碼），更新 <see cref="TerminalBuffer"/>。
/// 支援 C0 控制字元、CSI（游標/清除/捲動/插入刪除/模式/SGR）、OSC（標題）、DCS（忽略）。
/// </summary>
public class TerminalParser
{
    private enum State
    {
        Ground,
        Escape,
        EscapeIntermediate,
        CsiEntry,
        CsiParam,
        CsiIntermediate,
        CsiIgnore,
        OscString,
        DcsIgnore,
    }

    private readonly TerminalBuffer _buffer;
    private State _state = State.Ground;
    private readonly StringBuilder _params = new();
    private readonly StringBuilder _osc = new();
    private byte[] _leftover = Array.Empty<byte>();
    private byte[] _combinedBuf = new byte[16384];
    private bool _stPending;

    /// <summary>OSC 0/2 設定視窗標題時觸發。</summary>
    public event Action<string>? TitleChanged;

    public TerminalParser(TerminalBuffer buffer)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
    }

    /// <summary>餵入一段 byte（可跨多次呼叫；UTF-8 多 byte 字元會正確拼接）。</summary>
    public void Feed(byte[] data, int offset, int count)
    {
        int total = _leftover.Length + count;
        if (_combinedBuf.Length < total)
            _combinedBuf = new byte[Math.Max(total, _combinedBuf.Length * 2)];
        Array.Copy(_leftover, 0, _combinedBuf, 0, _leftover.Length);
        Array.Copy(data, offset, _combinedBuf, _leftover.Length, count);

        int incomplete = IncompleteTailLength(_combinedBuf, 0, total);
        int completeLen = total - incomplete;

        if (completeLen > 0)
        {
            string text = Encoding.UTF8.GetString(_combinedBuf, 0, completeLen);
            foreach (char c in text) ProcessChar(c);
        }

        if (incomplete > 0)
        {
            _leftover = new byte[incomplete];
            Array.Copy(_combinedBuf, completeLen, _leftover, 0, incomplete);
        }
        else
        {
            _leftover = Array.Empty<byte>();
        }
    }

    /// <summary>回傳結尾處「不完整的 UTF-8 序列」佔用的 byte 數（0 表示全部完整）。</summary>
    private static int IncompleteTailLength(byte[] buf, int offset, int count)
    {
        int end = offset + count;
        for (int back = 1; back <= Math.Min(4, count); back++)
        {
            byte b = buf[end - back];
            int seqLen;
            // 注意：mask 必須由最具體（最多 bit）往最寬鬆檢查，
            // 否則 3-byte lead (1110xxxx) 會先命中 (b & 0xC0) == 0xC0 被誤判成 2-byte。
            if ((b & 0x80) == 0) seqLen = 1;         // 0xxxxxxx ASCII
            else if ((b & 0xE0) == 0xC0) seqLen = 2;  // 110xxxxx
            else if ((b & 0xF0) == 0xE0) seqLen = 3;  // 1110xxxx
            else if ((b & 0xF8) == 0xF0) seqLen = 4;  // 11110xxx
            else continue;                             // 10xxxxxx continuation byte，繼續往前找
            return back < seqLen ? back : 0;
        }
        return 0;
    }

    private void ProcessChar(char c)
    {
        if (_stPending)
        {
            _stPending = false;
            if (c == '\\')
            {
                if (_state == State.OscString) FinalizeOsc();
                _state = State.Ground;
                return;
            }
            // 不是 ST：ESC 是新一個序列的開頭，回到 Ground 重新處理此字元
            _state = State.Ground;
        }

        switch (_state)
        {
            case State.Ground:
                if (c == 0x1b)
                {
                    _state = State.Escape;
                    _params.Clear();
                }
                else
                {
                    ProcessGround(c);
                }
                break;

            case State.Escape:
                if (c == '[')
                {
                    _state = State.CsiEntry;
                    _params.Clear();
                }
                else if (c == ']')
                {
                    _state = State.OscString;
                    _osc.Clear();
                }
                else if (c == 'P')
                {
                    _state = State.DcsIgnore;
                }
                else if (c >= 0x20 && c <= 0x2f)
                {
                    _state = State.EscapeIntermediate;
                }
                else
                {
                    _state = State.Ground; // 兩字元 escape 或無效，忽略
                }
                break;

            case State.EscapeIntermediate:
                if (c >= 0x30 && c <= 0x7e) _state = State.Ground;
                break;

            case State.CsiEntry:
                if (c == '?' || c == '>' || c == '=' || c == '<')
                {
                    _state = State.CsiParam;
                }
                else if (c >= '0' && c <= '9')
                {
                    _params.Append(c);
                    _state = State.CsiParam;
                }
                else if (c == ';')
                {
                    _params.Append(c);
                    _state = State.CsiParam;
                }
                else if (c >= 0x20 && c <= 0x2f)
                {
                    _state = State.CsiIntermediate;
                }
                else if (c >= 0x40 && c <= 0x7e)
                {
                    DispatchCsi(c);
                    _state = State.Ground;
                }
                else
                {
                    _state = State.CsiIgnore;
                }
                break;

            case State.CsiParam:
                if (c >= '0' && c <= '9') _params.Append(c);
                else if (c == ';') _params.Append(c);
                else if (c == '?' || c == '>' || c == '=' || c == '<') { /* private marker，忽略 */ }
                else if (c >= 0x20 && c <= 0x2f) _state = State.CsiIntermediate;
                else if (c >= 0x40 && c <= 0x7e)
                {
                    DispatchCsi(c);
                    _state = State.Ground;
                }
                else _state = State.CsiIgnore;
                break;

            case State.CsiIntermediate:
                if (c >= 0x40 && c <= 0x7e)
                {
                    DispatchCsi(c);
                    _state = State.Ground;
                }
                else if (c < 0x20 || c > 0x7e) _state = State.CsiIgnore;
                break;

            case State.CsiIgnore:
                if (c >= 0x40 && c <= 0x7e) _state = State.Ground;
                break;

            case State.OscString:
                if (c == 0x07)
                {
                    FinalizeOsc();
                    _state = State.Ground;
                }
                else if (c == 0x1b) _stPending = true;
                else _osc.Append(c);
                break;

            case State.DcsIgnore:
                if (c == 0x07) _state = State.Ground;
                else if (c == 0x1b) _stPending = true;
                break;
        }
    }

    private void ProcessGround(char c)
    {
        switch (c)
        {
            case '\b': _buffer.Backspace(); break;
            case '\t': _buffer.Tab(); break;
            case '\n':
            case '\v':
            case '\f': _buffer.NewLine(); break;
            case '\r': _buffer.CarriageReturn(); break;
            default:
                if (c >= 0x20) _buffer.PutChar(c);
                break;
        }
    }

    private void DispatchCsi(char final)
    {
        int[] p = ParseParams();
        switch (final)
        {
            case 'A': _buffer.CursorMove(-Param(p, 0, 1), 0); break;
            case 'B': _buffer.CursorMove(Param(p, 0, 1), 0); break;
            case 'C': _buffer.CursorMove(0, Param(p, 0, 1)); break;
            case 'D': _buffer.CursorMove(0, -Param(p, 0, 1)); break;
            case 'E': _buffer.CursorNextLine(Param(p, 0, 1)); break;
            case 'F': _buffer.CursorPrevLine(Param(p, 0, 1)); break;
            case 'G':
            case '`': _buffer.CursorToCol(Param(p, 0, 1)); break;
            case 'd': _buffer.CursorToRow(Param(p, 0, 1)); break;
            case 'H':
            case 'f': _buffer.CursorPosition(Param(p, 0, 1), Param(p, 1, 1)); break;
            case 'J': _buffer.EraseDisplay(Param(p, 0, 0)); break;
            case 'K': _buffer.EraseLine(Param(p, 0, 0)); break;
            case 'L': _buffer.InsertLine(Param(p, 0, 1)); break;
            case 'M': _buffer.DeleteLine(Param(p, 0, 1)); break;
            case '@': _buffer.InsertChars(Param(p, 0, 1)); break;
            case 'P':
            case 'X': _buffer.DeleteChars(Param(p, 0, 1)); break;
            case 'S': _buffer.ScrollUp(Param(p, 0, 1)); break;
            case 'T': _buffer.ScrollDown(Param(p, 0, 1)); break;
            case 'r': _buffer.SetScrollRegion(Param(p, 0, 1), Param(p, 1, _buffer.Rows)); break;
            case 'h': foreach (int m in p) _buffer.SetMode(m, on: true); break;
            case 'l': foreach (int m in p) _buffer.SetMode(m, on: false); break;
            case 'm': _buffer.ApplySgr(p); break;
            case 's': _buffer.SaveCursor(); break;
            case 'u': _buffer.RestoreCursor(); break;
            default: break;
        }
    }

    private int[] ParseParams()
    {
        if (_params.Length == 0) return Array.Empty<int>();
        var parts = _params.ToString().Split(';');
        var result = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            result[i] = int.TryParse(parts[i], out int v) ? v : 0;
        return result;
    }

    private static int Param(int[] p, int index, int def)
        => index < p.Length && p[index] != 0 ? p[index] : def;

    private void FinalizeOsc()
    {
        string s = _osc.ToString();
        int idx = s.IndexOf(';');
        if (idx > 0)
        {
            string code = s[..idx];
            string title = s[(idx + 1)..];
            if (code == "0" || code == "2")
                TitleChanged?.Invoke(title);
        }
    }
}
