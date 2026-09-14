using System.IO;
using System.Text.Json;

namespace BIOSForwardManager.Models;

/// <summary>
/// 終端機外觀/行為設定（字型、顏色、scrollback、游標）。
/// JSON 持久化到 exe 目錄下的 terminal_settings.json，重開程式自動載入。
/// </summary>
public class TerminalSettings
{
    public string FontFamily { get; set; } = "Consolas";
    public double FontSize { get; set; } = 14;
    public bool Bold { get; set; } = false;

    /// <summary>背景色（hex，例 "#1E1E1E"）。</summary>
    public string Background { get; set; } = "#1E1E1E";
    /// <summary>預設前景色（hex，例 "#DCDCDC"）。</summary>
    public string Foreground { get; set; } = "#DCDCDC";
    /// <summary>游標色（hex）。</summary>
    public string CursorColor { get; set; } = "#DCDCDC";

    /// <summary>scrollback 上限列數。</summary>
    public int ScrollbackLines { get; set; } = 5000;
    /// <summary>游標是否閃爍。</summary>
    public bool CursorBlink { get; set; } = true;

    private static string SettingsPath =>
        Path.Combine(AppContext.BaseDirectory, "terminal_settings.json");

    /// <summary>載入設定（檔案不存在/損毀時回傳預設值）。</summary>
    public static TerminalSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = JsonSerializer.Deserialize<TerminalSettings>(File.ReadAllText(SettingsPath));
                if (s != null) return s;
            }
        }
        catch { /* 損毀則用預設 */ }
        return new TerminalSettings();
    }

    /// <summary>存檔（失敗不影響使用）。</summary>
    public void Save()
    {
        try
        {
            File.WriteAllText(SettingsPath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 存檔失敗忽略 */ }
    }

    /// <summary>複製一份（供 dialog 編輯，不影響原物件）。</summary>
    public TerminalSettings Clone() => new()
    {
        FontFamily = FontFamily,
        FontSize = FontSize,
        Bold = Bold,
        Background = Background,
        Foreground = Foreground,
        CursorColor = CursorColor,
        ScrollbackLines = ScrollbackLines,
        CursorBlink = CursorBlink,
    };
}
