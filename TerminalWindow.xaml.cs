using System.Windows;
using System.Windows.Threading;
using Renci.SshNet;
using Renci.SshNet.Common;
using BIOSForwardManager.Models;
using BIOSForwardManager.Services;

namespace BIOSForwardManager;

/// <summary>
/// SSH 互動終端機視窗：宿主 <see cref="Controls.TerminalControl"/>（grid-based 終端機模擬器）。
/// 遠端輸出 → ShellStream.DataReceived 事件 → Term.Write；
/// 鍵盤 → Term.Input → shell.Write(string)（立即發送）；
/// 視窗 resize → Term 重算 rows/cols → SendWindowChange (SIGWINCH)。
/// </summary>
public partial class TerminalWindow : Window
{
    private readonly ShellStream _shell;
    private readonly SshService _ssh;

    public TerminalWindow(ShellStream shell, SshService ssh, string title)
    {
        InitializeComponent();
        _shell = shell;
        _ssh = ssh;
        Title = title;

        // 遠端輸出 → 終端機。
        // 注意：ShellStream.Read 是非阻塞的（無資料時立即回 0），不能用讀取迴圈；
        // 必須訂閱 DataReceived 事件（channel 每收到資料就觸發）。
        _shell.DataReceived += OnShellDataReceived;

        // 鍵盤輸入 → shell。
        // 注意：必須用 Write(string)（立即 SendData）；
        // Write(byte[],int,int) 會先緩衝、累積到 4096 bytes 才發送，單一字元永遠送不出去。
        Term.Input += input =>
        {
            try
            {
                if (_shell.CanWrite) _shell.Write(input);
            }
            catch { /* shell 已斷 */ }
        };

        // resize → SIGWINCH（通知遠端 PTY 新尺寸）
        Term.ShellSizeChanged += (rows, cols) =>
        {
            _ssh.SendWindowChange(_shell, (uint)cols, (uint)rows, (uint)(cols * 8), (uint)(rows * 17));
        };

        Loaded += (_, _) =>
        {
            Topmost = true;
            Activate();
            Term.Focus();
            Dispatcher.BeginInvoke(new Action(() => Topmost = false), DispatcherPriority.Background);
        };

        // 套用已存的終端機設定（字型/顏色/scrollback/游標）
        try { Term.ApplySettings(TerminalSettings.Load()); } catch { /* 用預設 */ }

        // scroll bar 同步（滾輪/新輸出/resize → 更新 bar；拖 bar → 設定 offset）
        Term.ScrollStateChanged += SyncScrollBar;
        SyncScrollBar();
    }

    /// <summary>把 scroll bar 同步到終端機捲動狀態（offset / scrollback 數量 / 可見列數）。</summary>
    private void SyncScrollBar()
    {
        int max = Term.ScrollbackCount;
        Scroll.Maximum = max;
        Scroll.ViewportSize = Math.Max(1, Term.VisibleRows);
        // 反向映射：scrollbar Value 從「頂端」算，_scrollOffset 從「底部」算。
        // _scrollOffset=0 (live/底部) → Value=max (thumb 在最下)；_scrollOffset=max (歷史頂端) → Value=0 (thumb 在最上)。
        Scroll.Value = max - Math.Min(Term.ScrollOffset, max);
        // 內容沒超過螢幕（無 scrollback）時隱藏 scroll bar
        Scroll.Visibility = max > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>使用者拖曳/點擊 scroll bar → 設定終端機捲動偏移。</summary>
    // 注意：ScrollBar.Scroll 事件的 delegate 是 ScrollEventHandler，參數是 Primitives.ScrollEventArgs
    //（不是 ScrollViewer 用的 ScrollChangedEventArgs）。
    private void OnScrollChanged(object sender, System.Windows.Controls.Primitives.ScrollEventArgs e)
    {
        // 反向映射：Value 從頂端算 → _scrollOffset 從底部算
        int max = Term.ScrollbackCount;
        Term.SetScrollOffset(max - (int)Math.Round(e.NewValue));
    }

    /// <summary>
    /// 清除畫面：送 Ctrl+L（\x0C）給 shell — bash/zsh 的 readline 會清屏並重繪 prompt。
    /// 不能只清本地 buffer：遠端游標狀態會不同步，之後輸出會錯位。
    /// scrollback（歷史）保留，仍可往上捲。
    /// </summary>
    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        try { if (_shell.CanWrite) _shell.Write("\x0C"); } catch { /* shell 已斷 */ }
    }

    /// <summary>開啟終端機設定對話框；確定後即時套用並存檔。</summary>
    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var dlg = new TerminalSettingsDialog(TerminalSettings.Load()) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            Term.ApplySettings(dlg.Result);
            dlg.Result.Save();
        }
    }

    private void OnShellDataReceived(object? sender, ShellDataEventArgs e)
    {
        if (e.Data == null || e.Data.Length == 0) return;
        try
        {
            Dispatcher.Invoke(() => Term.Write(e.Data, 0, e.Data.Length));
        }
        catch { /* 視窗已關閉 */ }
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _shell.DataReceived -= OnShellDataReceived;
        try { _shell.Dispose(); } catch { }
    }
}
