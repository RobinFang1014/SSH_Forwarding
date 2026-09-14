using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BIOSForwardManager.Models;
using BIOSForwardManager.Services;
using BIOSForwardManager.ViewModels;

namespace BIOSForwardManager;

/// <summary>Interaction logic for MainWindow.xaml</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    // 欄寬拖曳狀態
    private int _resizingColumn = -1;
    private Point _resizeStartPoint;
    private double _resizeStartWidth;

    // 拖曳排序狀態
    private ListViewItem? _dropTarget;
    private bool _dropAbove;
    private bool _isDragging;
    private DispatcherTimer? _dragScrollTimer;
    private Point _lastDragPos; // 拖曳中最後已知的滑鼠位置（ListView 座標）
    private IntPtr _wheelHookId = IntPtr.Zero;
    private LowLevelMouseProc? _wheelHookProc; // 必須持有參考，否則 delegate 被 GC 回收會當機
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEWHEEL = 0x020A;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
    }

    private ColumnWidths ColWidths => (ColumnWidths)FindResource("ColWidths");

    // ===== 欄寬拖曳（與檔案總管一致的流暢調整） =====

    private void OnSplitterMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement splitter) return;
        // XAML 中 Tag="0" 是字串，必須用 Convert 轉換
        _resizingColumn = Convert.ToInt32(splitter.Tag);
        _resizeStartPoint = e.GetPosition(this);
        _resizeStartWidth = GetColumnWidth(_resizingColumn);
        splitter.CaptureMouse();
        e.Handled = true;
    }

    private void OnSplitterMouseMove(object sender, MouseEventArgs e)
    {
        if (_resizingColumn < 0) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(this);
        double delta = pos.X - _resizeStartPoint.X;
        SetColumnWidth(_resizingColumn, _resizeStartWidth + delta);
    }

    private void OnSplitterMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_resizingColumn < 0) return;
        if (sender is FrameworkElement splitter)
            splitter.ReleaseMouseCapture();
        _resizingColumn = -1;
    }

    private double GetColumnWidth(int col) => col switch
    {
        0 => ColWidths.NameWidth,
        1 => ColWidths.TypeWidth,
        2 => ColWidths.LPortWidth,
        3 => ColWidths.RPortWidth,
        4 => ColWidths.StatusWidth,
        _ => 0
    };

    private void SetColumnWidth(int col, double width)
    {
        switch (col)
        {
            case 0: ColWidths.NameWidth = width; break;
            case 1: ColWidths.TypeWidth = width; break;
            case 2: ColWidths.LPortWidth = width; break;
            case 3: ColWidths.RPortWidth = width; break;
            case 4: ColWidths.StatusWidth = width; break;
        }
    }

    // ===== 項目互動 =====

    /// <summary>點擊展開/折疊鈕時，同時選取該項目。</summary>
    private void OnToggleClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ForwardItem item)
            ForwardListView.SelectedItem = item;
    }

    // ===== 拖曳排序（僅 IP 分組可拖曳） =====

    private void OnListViewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 點在按鈕（展開鈕 / Action）上不啟動拖曳
        if (e.OriginalSource is DependencyObject src && FindVisualParent<ButtonBase>(src) != null) return;
        if (e.OriginalSource is not DependencyObject dep) return;
        if (FindVisualParent<ListViewItem>(dep) is not { } lvi) return;
        if (lvi.DataContext is not ForwardItem item) return;
        if (item.ItemType != ForwardItemType.Group) return;

        var data = new DataObject("ForwardItem", item);
        _lastDragPos = e.GetPosition(ForwardListView);
        StartDragAutoScroll();
        try
        {
            DragDrop.DoDragDrop(ForwardListView, data, DragDropEffects.Move);
        }
        finally
        {
            StopDragAutoScroll();
            ClearDropIndicator();
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        _lastDragPos = e.GetPosition(ForwardListView); // 無論在項目上或空白區都要更新（供邊緣自動捲動用）
        if (!e.Data.GetDataPresent("ForwardItem")) return;
        if (e.OriginalSource is not DependencyObject dep) return;
        if (FindVisualParent<ListViewItem>(dep) is not { } lvi) return;
        if (lvi.DataContext is not ForwardItem item) return;

        ForwardItem? targetGroup = item.ItemType == ForwardItemType.Group ? item : item.Parent;
        if (targetGroup == null) return;

        e.Effects = DragDropEffects.Move;

        Point pos = e.GetPosition(lvi);
        bool above = pos.Y < lvi.ActualHeight / 2;

        if (!ReferenceEquals(_dropTarget, lvi) || _dropAbove != above)
        {
            _dropTarget = lvi;
            _dropAbove = above;
            UpdateDropIndicator(lvi, above);
        }
    }

    private void UpdateDropIndicator(ListViewItem lvi, bool above)
    {
        Point p = lvi.TranslatePoint(new Point(0, 0), ForwardListView);
        double y = above ? p.Y : p.Y + lvi.ActualHeight;
        DropIndicatorLine.Margin = new Thickness(0, y, 0, 0);
        DropIndicatorLine.Visibility = Visibility.Visible;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        ClearDropIndicator();
        if (!e.Data.GetDataPresent("ForwardItem")) return;
        if (e.OriginalSource is not DependencyObject dep) return;
        if (FindVisualParent<ListViewItem>(dep) is not { } lvi) return;
        if (lvi.DataContext is not ForwardItem item) return;

        if (e.Data.GetData("ForwardItem") is not ForwardItem dragged) return;
        ForwardItem? targetGroup = item.ItemType == ForwardItemType.Group ? item : item.Parent;
        if (targetGroup == null) return;

        Point pos = e.GetPosition(lvi);
        bool above = pos.Y < lvi.ActualHeight / 2;

        _vm.MoveGroup(dragged, targetGroup, above);
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        ClearDropIndicator();
    }

    private void ClearDropIndicator()
    {
        DropIndicatorLine.Visibility = Visibility.Collapsed;
        _dropTarget = null;
        _dropAbove = false;
    }

    // ===== 拖曳期間的捲動支援（WPF 內建拖曳不會自動捲動） =====

    private ScrollViewer? GetListViewScrollViewer() => FindVisualChild<ScrollViewer>(ForwardListView);

    private void StartDragAutoScroll()
    {
        _isDragging = true;
        _dragScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _dragScrollTimer.Tick += (_, _) => DragAutoScrollTick();
        _dragScrollTimer.Start();

        // DoDragDrop 的 OLE 拖曳迴圈期間，WPF MouseWheel 與視窗的 WM_MOUSEWHEEL 都收不到，
        // 必須用全域 low-level mouse hook（WH_MOUSE_LL）在系統層攔截滾輪輸入
        _wheelHookProc = WheelHookCallback;
        _wheelHookId = SetWindowsHookEx(WH_MOUSE_LL, _wheelHookProc, GetModuleHandle(null), 0);
    }

    private void StopDragAutoScroll()
    {
        _isDragging = false;
        _dragScrollTimer?.Stop();
        _dragScrollTimer = null;
        if (_wheelHookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_wheelHookId);
            _wheelHookId = IntPtr.Zero;
            _wheelHookProc = null;
        }
    }

    /// <summary>全域 low-level mouse hook：拖曳期間、滑鼠在列表範圍內時，把滾輪轉成 ScrollViewer 捲動。</summary>
    private IntPtr WheelHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == WM_MOUSEWHEEL && _isDragging)
        {
            var info = Marshal.PtrToStructure<MOUSEHOOKSTRUCT>(lParam);
            var topLeft = ForwardListView.PointToScreen(new Point(0, 0));
            var rect = new Rect(topLeft, new Size(ForwardListView.ActualWidth, ForwardListView.ActualHeight));
            if (rect.Contains(new Point(info.pt.x, info.pt.y)))
            {
                int delta = (short)((info.mouseData >> 16) & 0xFFFF); // 通常 ±120
                var sv = GetListViewScrollViewer();
                if (sv != null && delta != 0)
                {
                    sv.ScrollToVerticalOffset(sv.VerticalOffset - delta / 2); // 120px/notch 太快，折半
                    return (IntPtr)1; // 已處理，不再傳給其他 hook/視窗
                }
            }
        }
        return CallNextHookEx(_wheelHookId, nCode, wParam, lParam);
    }

    // ===== Win32 low-level mouse hook P/Invoke =====

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    /// <summary>拖曳中滑鼠靠近列表上/下緣（40px 內）時自動捲動，越靠近邊緣越快。</summary>
    private void DragAutoScrollTick()
    {
        var sv = GetListViewScrollViewer();
        if (sv == null) return;

        Point local = _lastDragPos; // 滑鼠停在邊緣不動時，持續用最後位置捲動
        const double edge = 40;

        double delta = 0;
        if (local.Y < edge)
            delta = -(edge * (1.0 - local.Y / edge)) - 4;
        else if (local.Y > ForwardListView.ActualHeight - edge)
            delta = edge * (1.0 - (ForwardListView.ActualHeight - local.Y) / edge) + 4;

        if (delta != 0)
            sv.ScrollToVerticalOffset(sv.VerticalOffset + delta);
    }

    /// <summary>拖曳期間滾輪事件被 ListView capture 收走（到不了內部 ScrollViewer），手動轉發。</summary>
    private void OnListViewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_isDragging) return; // 非拖曳時走預設捲動
        var sv = GetListViewScrollViewer();
        if (sv == null) return;
        sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var result = FindVisualChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent) return parent;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    /// <summary>X 按鈕 → 關閉視窗 → OnLastWindowClose 自動結束程式。</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized && !TrayHelper.IsExiting)
            TrayHelper.Instance?.MinimizeToTray(this);
    }

    /// <summary>視窗關閉時清理 SSH Master。</summary>
    protected override void OnClosed(EventArgs e)
    {
        try
        {
            _vm.Cleanup();
        }
        catch { }
        base.OnClosed(e);
    }
}
