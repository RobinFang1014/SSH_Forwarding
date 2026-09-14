using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BIOSForwardManager.Models;
using WinForms = System.Windows.Forms;

namespace BIOSForwardManager;

/// <summary>
/// 終端機設定對話框（Tera Term 式多分頁）：字型 / 顏色 / 終端機行為。
/// 即時預覽；確定後透過 <see cref="Result"/> 回傳設定。
/// </summary>
public partial class TerminalSettingsDialog : Window
{
    private readonly TerminalSettings _result;

    /// <summary>確定後回傳的設定（Clone 過的副本）。</summary>
    public TerminalSettings Result => _result;

    // 等寬字型優先顯示
    private static readonly string[] MonoFonts =
    {
        "Cascadia Mono", "Cascadia Code", "Consolas", "Courier New",
        "Lucida Console", "NSimSun", "Microsoft JhengHei", "PMingLiU",
    };

    public TerminalSettingsDialog(TerminalSettings current)
    {
        InitializeComponent();
        _result = current.Clone();

        // 字型清單：只列「等寬字型」（實際量測字元寬度判斷）。
        // 終端機是 grid-based（每字元固定一格），比例字型（微軟正黑體等）會導致字元錯位、中間出現奇怪空白。
        double dpi = 1.0;
        try { dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }
        var mono = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .Distinct()
            .Where(f => Controls.TerminalControl.IsMonospaceFont(f, dpi))
            .OrderBy(f => MonoFonts.Contains(f) ? 0 : 1)
            .ThenBy(f => f)
            .ToList();
        if (mono.Count == 0) mono = new List<string> { "Consolas" };
        FontCombo.ItemsSource = mono;
        FontCombo.SelectedItem = mono.Contains(_result.FontFamily) ? _result.FontFamily : "Consolas";
        if (FontCombo.SelectedItem == null) FontCombo.SelectedIndex = 0;

        SizeSlider.Value = _result.FontSize;
        SizeLabel.Text = ((int)_result.FontSize).ToString();
        BoldCheck.IsChecked = _result.Bold;

        BgHex.Text = _result.Background;
        FgHex.Text = _result.Foreground;
        CurHex.Text = _result.CursorColor;
        BgBtn.Background = MakeBrush(_result.Background);
        FgBtn.Background = MakeBrush(_result.Foreground);
        CurBtn.Background = MakeBrush(_result.CursorColor);

        ScrollSlider.Value = Math.Clamp(_result.ScrollbackLines, 100, 20000);
        ScrollLabel.Text = ((int)ScrollSlider.Value).ToString();
        BlinkCheck.IsChecked = _result.CursorBlink;

        // 即時預覽綁定
        FontCombo.SelectionChanged += (_, _) => UpdatePreview();
        SizeSlider.ValueChanged += (_, _) =>
        {
            SizeLabel.Text = ((int)SizeSlider.Value).ToString();
            UpdatePreview();
        };
        BoldCheck.Checked += (_, _) => UpdatePreview();
        BoldCheck.Unchecked += (_, _) => UpdatePreview();
        ScrollSlider.ValueChanged += (_, _) =>
            ScrollLabel.Text = ((int)ScrollSlider.Value).ToString();

        UpdatePreview();
    }

    private static Brush MakeBrush(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return Brushes.Gray; }
    }

    private static Color ParseHex(string hex, Color fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return fallback; }
    }

    /// <summary>用目前選定的字型/大小/粗體/顏色重繪預覽區。</summary>
    private void UpdatePreview()
    {
        var family = new FontFamily(FontCombo.SelectedItem as string ?? "Consolas");
        var weight = BoldCheck.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
        var fg = ParseHex(FgHex.Text, Colors.White);
        var bg = ParseHex(BgHex.Text, Colors.Black);

        PreviewBorder.Background = new SolidColorBrush(bg);
        foreach (var tb in new[] { Pv1, Pv2, Pv3, Pv4 })
        {
            tb.FontFamily = family;
            tb.FontSize = SizeSlider.Value;
            tb.FontWeight = weight;
        }
        // 模擬 prompt：user@host 綠色（明確色，不受前景色影響）、其餘用前景色
        PvUser.Foreground = new SolidColorBrush(Color.FromRgb(0x0D, 0xBC, 0x79));
        PvPath.Foreground = new SolidColorBrush(fg);
        PvCmd.Foreground = new SolidColorBrush(fg);
        Pv2.Foreground = new SolidColorBrush(fg);
        Pv3.Foreground = new SolidColorBrush(fg);
        Pv4.Foreground = new SolidColorBrush(fg);
    }

    // ===== 取色 =====

    private void OnPickBg(object sender, RoutedEventArgs e) => PickColor(BgBtn, BgHex);
    private void OnPickFg(object sender, RoutedEventArgs e) => PickColor(FgBtn, FgHex);
    private void OnPickCur(object sender, RoutedEventArgs e) => PickColor(CurBtn, CurHex);

    private void PickColor(Button btn, TextBlock hexLabel)
    {
        var current = btn.Background is SolidColorBrush sb ? sb.Color : Colors.White;
        using var dlg = new WinForms.ColorDialog
        {
            // WinForms ColorDialog 用 System.Drawing.Color，需從 WPF Color 轉換
            Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B),
            FullOpen = true,
        };
        if (dlg.ShowDialog() == WinForms.DialogResult.OK)
        {
            var c = dlg.Color; // System.Drawing.Color（R/G/B 為 byte）
            btn.Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
            hexLabel.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            UpdatePreview();
        }
    }

    // ===== 確定 / 取消 =====

    private void OnOk(object sender, RoutedEventArgs e)
    {
        _result.FontFamily = FontCombo.SelectedItem as string ?? "Consolas";
        _result.FontSize = SizeSlider.Value;
        _result.Bold = BoldCheck.IsChecked == true;
        _result.Background = BgHex.Text;
        _result.Foreground = FgHex.Text;
        _result.CursorColor = CurHex.Text;
        _result.ScrollbackLines = (int)ScrollSlider.Value;
        _result.CursorBlink = BlinkCheck.IsChecked == true;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
