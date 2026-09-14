using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace BIOSForwardManager.Services;

/// <summary>
/// 系統 Tray（右下角）管理：NotifyIcon + 主視窗的最小化/還原/結束。
/// 使用內建 WinForms NotifyIcon（零額外套件）。
/// </summary>
public class TrayHelper
{
    private WinForms.NotifyIcon? _notifyIcon;
    private Window? _mainWindow;

    /// <summary>全域單例（供視窗 code-behind 存取）。</summary>
    public static TrayHelper? Instance { get; private set; }

    /// <summary>是否正在真正結束（視窗 OnClosing 檢查此旗標以決定是否縮到 tray）。</summary>
    public static bool IsExiting { get; private set; }

    public void Initialize(Window mainWindow)
    {
        Instance = this;
        _mainWindow = mainWindow;
        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = GetStateIcon(false), // 初始狀態：未連線（灰點）
            Visible = true,
            Text = "BIOS Forward Manager"
        };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Show Main", null, (_, _) => Restore(_mainWindow));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Exit());
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => Restore(_mainWindow);
    }

    /// <summary>把視窗縮到 tray（隱藏）。</summary>
    public void MinimizeToTray(Window w)
    {
        if (w == null) return;
        w.WindowState = WindowState.Normal;
        w.Hide();
    }

    /// <summary>還原視窗。</summary>
    public void Restore(Window? w)
    {
        if (w == null) return;
        w.Show();
        w.WindowState = WindowState.Normal;
        w.Activate();
    }

    /// <summary>真正結束程式（清理 tray 圖示）。</summary>
    public void Exit()
    {
        IsExiting = true;
        try { _mainWindow?.Close(); } catch { }
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        Application.Current.Shutdown();
    }

    private Icon? _iconConnected;
    private Icon? _iconDisconnected;

    /// <summary>依連線狀態切換 tray icon（綠點=已連線、灰點=未連線）與 tooltip。</summary>
    public void SetConnected(bool connected)
    {
        if (_notifyIcon == null) return;
        try
        {
            _notifyIcon.Icon = GetStateIcon(connected);
            _notifyIcon.Text = connected ? "BIOS Forward Manager - Connected" : "BIOS Forward Manager";
        }
        catch { }
    }

    private Icon GetStateIcon(bool connected)
        => connected ? _iconConnected ??= CreateStateIcon(true)
                     : _iconDisconnected ??= CreateStateIcon(false);

    /// <summary>產生狀態 icon：base icon（link.png）+ 右下角狀態圓點（綠=連線、灰=未連線）。</summary>
    private static Icon CreateStateIcon(bool connected)
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/link.png", UriKind.Absolute);
            var stream = Application.GetResourceStream(uri)?.Stream;
            if (stream == null) return SystemIcons.Application;

            using var baseBmp = new Bitmap(stream);
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.DrawImage(baseBmp, 0, 0, 32, 32);
                var color = connected ? Color.FromArgb(46, 204, 64) : Color.FromArgb(136, 136, 136);
                using var brush = new SolidBrush(color);
                using var pen = new Pen(Color.FromArgb(30, 30, 30), 1.5f);
                g.FillEllipse(brush, 19, 19, 12, 12);
                g.DrawEllipse(pen, 19, 19, 12, 12);
            }
            IntPtr hIcon = bmp.GetHicon();
            try
            {
                using var ms = new MemoryStream();
                using (var tmp = Icon.FromHandle(hIcon)) tmp.Save(ms);
                ms.Position = 0;
                return new Icon(ms);
            }
            finally { DestroyIcon(hIcon); }
        }
        catch { return SystemIcons.Application; }
    }

    private static Icon LoadTrayIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/link.png", UriKind.Absolute);
            var stream = Application.GetResourceStream(uri)?.Stream;
            if (stream != null)
            {
                using var src = new Bitmap(stream);
                using var resized = new Bitmap(src, new System.Drawing.Size(32, 32));
                IntPtr hIcon = resized.GetHicon();
                using var ms = new MemoryStream();
                using (var tmp = Icon.FromHandle(hIcon))
                {
                    tmp.Save(ms);
                }
                DestroyIcon(hIcon);
                ms.Position = 0;
                return new Icon(ms);
            }
        }
        catch { }
        return SystemIcons.Application;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
