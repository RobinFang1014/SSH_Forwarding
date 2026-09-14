using System.Windows.Media;

namespace BIOSForwardManager.Terminal;

/// <summary>終端機單一格子：字元 + 前景/背景色 + 文字屬性。</summary>
public readonly struct TerminalCell
{
    public char Ch { get; }
    public Color Fg { get; }
    public Color Bg { get; }
    public bool Bold { get; }
    public bool Italic { get; }
    public bool Underline { get; }
    public bool Inverse { get; }

    public TerminalCell(char ch, Color fg, Color bg,
        bool bold = false, bool italic = false, bool underline = false, bool inverse = false)
    {
        Ch = ch;
        Fg = fg;
        Bg = bg;
        Bold = bold;
        Italic = italic;
        Underline = underline;
        Inverse = inverse;
    }

    /// <summary>空白格子（指定顏色）。</summary>
    public static TerminalCell Default(Color fg, Color bg) => new(' ', fg, bg);

    public TerminalCell WithChar(char ch) => new(ch, Fg, Bg, Bold, Italic, Underline, Inverse);

    public TerminalCell WithStyle(Color fg, Color bg, bool bold, bool italic, bool underline, bool inverse)
        => new(Ch, fg, bg, bold, italic, underline, inverse);
}
