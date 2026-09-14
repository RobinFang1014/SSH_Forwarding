using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using BIOSForwardManager.Models;
using BIOSForwardManager.Services;

namespace BIOSForwardManager.ViewModels;

/// <summary>
/// 主視窗 ViewModel：管理依 Remote IP 分組的樹狀資料、SSH 指令、狀態刷新。
/// 使用 INotifyPropertyChanged + ObservableCollection，UI 自動刷新（無需手動 Refresh 按鈕）。
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private readonly ConfigService _config = new();
    private readonly SshService _ssh = new();
    private readonly DispatcherTimer _timer;
    private bool _suppressRebuild;

    public SshConfig Ssh { get; private set; } = new();
    public List<ForwardConfig> Configs { get; private set; } = new();

    /// <summary>依展開狀態扁平化後、實際顯示的項目（供 ListView 綁定）。</summary>
    public ObservableCollection<ForwardItem> VisibleItems { get; } = new();

    /// <summary>頂層 IP 分組。</summary>
    public List<ForwardItem> Groups { get; } = new();

    private ForwardItem? _selectedItem;
    public ForwardItem? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusBrush));
                OnPropertyChanged(nameof(ConnectButtonText));
                // 立即刷新所有 Command 的 CanExecute（讓 Terminal/StartAuto/ClearAll 按鈕在連線狀態改變時
                // 即時啟用/停用，不需等使用者下一次 UI 互動才 requery）
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                // 同步切換 tray icon 狀態（綠點=已連線、灰點=未連線）
                TrayHelper.Instance?.SetConnected(value);
            }
        }
    }

    public string StatusText => IsConnected ? "Connected (Master)" : "Disconnected";
    public System.Windows.Media.Brush StatusBrush => IsConnected ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.Gray;
    public string ConnectButtonText => IsConnected ? "Exit Master" : "Connect Master";

    private DateTime? _connectedAt;
    private string _uptimeText = "Uptime: 00h 00m";
    public string UptimeText { get => _uptimeText; private set => SetProperty(ref _uptimeText, value); }

    private string _resultText = "Result: Ready";
    public string ResultText { get => _resultText; private set => SetProperty(ref _resultText, value); }

    private Brush _resultBrush = Brushes.DarkBlue;
    public Brush ResultBrush { get => _resultBrush; private set => SetProperty(ref _resultBrush, value); }

    private int _totalCount;
    public int TotalCount { get => _totalCount; private set => SetProperty(ref _totalCount, value); }

    private int _activeCount;
    public int ActiveCount { get => _activeCount; private set => SetProperty(ref _activeCount, value); }

    // ===== 指令 =====
    public RelayCommand ConnectMasterCommand { get; }
    public RelayCommand StartAutoCommand { get; }
    public RelayCommand AddConnectionCommand { get; }
    public RelayCommand EditConnectionCommand { get; }
    public RelayCommand RemoveConnectionCommand { get; }
    public RelayCommand ClearAllCommand { get; }
    public RelayCommand ToggleForwardCommand { get; }
    public RelayCommand ExpandAllCommand { get; }
    public RelayCommand CollapseAllCommand { get; }
    public RelayCommand SshSettingsCommand { get; }
    public RelayCommand OpenConsoleCommand { get; }
    public RelayCommand ExitCommand { get; }

    public MainViewModel()
    {
        (Ssh, Configs) = _config.Load();
        BuildTree();

        ConnectMasterCommand = new RelayCommand(ConnectMaster);
        StartAutoCommand = new RelayCommand(StartAuto, () => IsConnected);
        AddConnectionCommand = new RelayCommand(AddConnection);
        EditConnectionCommand = new RelayCommand(EditConnection,
            () => SelectedItem is { IsConnection: true });
        RemoveConnectionCommand = new RelayCommand(RemoveConnection,
            () => SelectedItem is { IsConnection: true });
        ClearAllCommand = new RelayCommand(ClearAll, () => IsConnected);
        ToggleForwardCommand = new RelayCommand(
            p => ToggleForward(p as ForwardItem),
            p => p is ForwardItem { IsConnection: true });
        ExpandAllCommand = new RelayCommand(ExpandAll);
        CollapseAllCommand = new RelayCommand(CollapseAll);
        SshSettingsCommand = new RelayCommand(OpenSshSettings);
        OpenConsoleCommand = new RelayCommand(OpenConsole, () => IsConnected);
        ExitCommand = new RelayCommand(() => Application.Current.Shutdown());

        // 每 5 秒刷新一次 ACTIVE/INACTIVE 狀態與 Uptime（UI 自動更新）
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += async (_, _) =>
        {
            RefreshStatus();
            // 背景檢查 master 是否斷線
            if (IsConnected)
            {
                bool ready = await Task.Run(() => _ssh.IsMasterReady());
                if (!ready)
                    OnSshDisconnected();
            }
        };
        _timer.Start();

        // 非預期斷線即時通知（SSH.NET ErrorOccurred / 操作失敗發現斷線）— 不用等 5 秒輪詢
        _ssh.Disconnected += OnSshDisconnected;
    }

    // ===== 樹狀結構 =====

    /// <summary>依 Remote IP 分組建立樹狀結構（IP 為父節點，連線為子節點）。</summary>
    private void BuildTree()
    {
        Groups.Clear();
        // GroupBy 保留 Configs 中 IP「首次出現」的順序 → config.txt 的順序就是顯示順序
        //（不再依 IP 字母排序，否則拖曳排序後重開會跑回原來的順序）
        var grouped = Configs.GroupBy(c => c.RemoteIp);

        foreach (var g in grouped)
        {
            var group = new ForwardItem
            {
                ItemType = ForwardItemType.Group,
                Name = g.Key,
                Ip = g.Key,
                Level = 0,
                IsExpanded = true
            };
            foreach (var c in g.OrderBy(x => x.LocalPort))
            {
                group.Children.Add(new ForwardItem
                {
                    ItemType = ForwardItemType.Connection,
                    Name = c.Name,
                    Type = c.Type,
                    LocalPort = c.LocalPort,
                    RemotePort = c.RemotePort,
                    Auto = c.Auto,
                    Config = c,
                    Level = 1,
                    Parent = group
                });
            }
            group.PropertyChanged += OnItemPropertyChanged;
            foreach (var child in group.Children)
                child.PropertyChanged += OnItemPropertyChanged;
            Groups.Add(group);
        }

        SelectedItem = null;
        RebuildVisibleList();
    }

    /// <summary>依展開狀態重建可見清單（扁平化）。</summary>
    private void RebuildVisibleList()
    {
        VisibleItems.Clear();
        foreach (var group in Groups)
        {
            VisibleItems.Add(group);
            if (group.IsExpanded)
                foreach (var child in group.Children)
                    VisibleItems.Add(child);
        }
        UpdateCounts();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressRebuild) return;
        if (e.PropertyName == nameof(ForwardItem.IsExpanded))
            RebuildVisibleList();
    }

    private void UpdateCounts()
    {
        TotalCount = Configs.Count;
        ActiveCount = VisibleItems.Count(i => i.IsConnection && i.IsActive);
    }

    // ===== 狀態刷新 =====

    /// <summary>刷新所有連線的 ACTIVE/INACTIVE 狀態、分組摘要、Uptime。</summary>
    public void RefreshStatus()
    {
        foreach (var group in Groups)
        {
            foreach (var child in group.Children)
            {
                if (child.Config == null) continue;
                bool active = _ssh.IsForwardActive(child.Config.LocalPort);
                if (child.IsActive != active)
                    child.IsActive = active;
            }
            group.NotifyGroupStatusChanged();
        }
        UpdateCounts();
        UpdateUptime();
    }

    private void UpdateUptime()
    {
        if (_connectedAt == null)
        {
            UptimeText = "Uptime: 00h 00m";
            return;
        }
        var uptime = DateTime.Now - _connectedAt.Value;
        int hours = (int)uptime.TotalHours;
        int minutes = (int)uptime.TotalMinutes % 60;
        UptimeText = $"Uptime: {hours:00}h {minutes:00}m";
    }

    // ===== 指令實作 =====

    private async void ConnectMaster()
    {
        if (!IsConnected)
        {
            if (string.IsNullOrWhiteSpace(Ssh.User) || string.IsNullOrWhiteSpace(Ssh.Host))
            {
                SetResult("Result: ERROR - SSH User/Host not configured. Use SSH Settings.", Brushes.Red);
                return;
            }

            // 彈出登入對話框
            var dialog = new LoginDialog { Owner = System.Windows.Application.Current.MainWindow };
            if (dialog.ShowDialog() != true) return;

            string password = dialog.Password;
            string totp = dialog.TotpCode;

            SetResult("Result: Connecting...", Brushes.DarkBlue);
            // 背景執行連線（避免凍結 UI）
            var (success, error) = await Task.Run(() => _ssh.Connect(Ssh, password, totp));

            if (success)
            {
                IsConnected = true;
                _connectedAt = DateTime.Now;
                SetResult("Result: Connected successfully.", Brushes.Green);
                StartAuto();
            }
            else
            {
                SetResult($"Result: FAILED - {error}", Brushes.Red);
            }
        }
        else
        {
            _ssh.StopMaster();
            IsConnected = false;
            _connectedAt = null;
            SetResult("Result: Disconnected.", Brushes.Blue);
        }
        RefreshStatus();
    }

    /// <summary>App 退出時清理：停止計時器 + 斷開 SSH。</summary>
    public void ShutdownSsh()
    {
        _timer.Stop();
        _ssh.Cleanup();
    }

    private void StartAuto()
    {
        if (!IsConnected) return;
        int count = 0;
        foreach (var cfg in Configs.Where(c => c.Auto))
        {
            if (_ssh.IsPortInUse(cfg.LocalPort)) continue;
            var (ok, _) = _ssh.EnableForward(Ssh, cfg);
            if (ok) count++;
        }
        RefreshStatus();
        SetResult($"Result: Auto-started {count} connections.", Brushes.Green);
    }

    private void AddConnection()
    {
        var dialog = new ConnectionDialog(null, Configs) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            Configs.Add(dialog.Result);
            SaveConfig();
            BuildTree();
            SetResult("Result: New connection added.", Brushes.Green);
        }
    }

    private void EditConnection()
    {
        if (SelectedItem is not { IsConnection: true } item || item.Config == null) return;
        var cfg = item.Config;
        if (_ssh.IsPortInUse(cfg.LocalPort))
        {
            MessageBox.Show("Cannot edit an active connection. Please disable it first.", "Edit Blocked");
            return;
        }
        var dialog = new ConnectionDialog(cfg, Configs) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            cfg.Type = dialog.Result.Type;
            cfg.Name = dialog.Result.Name;
            cfg.LocalPort = dialog.Result.LocalPort;
            cfg.RemoteIp = dialog.Result.RemoteIp;
            cfg.RemotePort = dialog.Result.RemotePort;
            cfg.Auto = dialog.Result.Auto;
            SaveConfig();
            BuildTree();
            SetResult("Result: Connection updated.", Brushes.Green);
        }
    }

    private void RemoveConnection()
    {
        if (SelectedItem is not { IsConnection: true } item || item.Config == null) return;
        Configs.Remove(item.Config);
        SaveConfig();
        BuildTree();
        SetResult("Result: Selected connection removed.", Brushes.Green);
    }

    private void ClearAll()
    {
        if (!IsConnected) return;
        int count = 0;
        foreach (var cfg in Configs)
        {
            if (_ssh.IsForwardActive(cfg.LocalPort))
            {
                _ssh.DisableForward(Ssh, cfg);
                count++;
            }
        }
        RefreshStatus();
        SetResult($"Result: Successfully cleared {count} active forwards.", Brushes.Green);
    }

    private void ToggleForward(ForwardItem? item)
    {
        if (item is not { IsConnection: true } || item.Config == null) return;
        if (!IsConnected)
        {
            SetResult("Result: ERROR - Connect Master first!", Brushes.Red);
            return;
        }
        var cfg = item.Config;
        bool isEnabling = !item.IsActive;

        // 防衝突：啟用前確認該 Port 沒有被佔用
        if (isEnabling && _ssh.IsPortInUse(cfg.LocalPort))
        {
            SetResult($"Result: ERROR - Local Port {cfg.LocalPort} is already in use!", Brushes.Red);
            return;
        }

        var (ok, output) = isEnabling
            ? _ssh.EnableForward(Ssh, cfg)
            : _ssh.DisableForward(Ssh, cfg);

        if (ok)
            SetResult($"Result: {(isEnabling ? "Enabled" : "Disabled")} {cfg.Name}.", Brushes.Green);
        else
            SetResult($"Result: ERROR - {output}", Brushes.Red);

        RefreshStatus();
    }

    private void ExpandAll()
    {
        _suppressRebuild = true;
        foreach (var g in Groups) g.IsExpanded = true;
        _suppressRebuild = false;
        RebuildVisibleList();
    }

    private void CollapseAll()
    {
        _suppressRebuild = true;
        foreach (var g in Groups) g.IsExpanded = false;
        _suppressRebuild = false;
        RebuildVisibleList();
    }

    /// <summary>拖曳排序：把 IP 分組移動到目標分組的前/後。</summary>
    public void MoveGroup(ForwardItem dragged, ForwardItem target, bool insertBefore)
    {
        if (dragged.ItemType != ForwardItemType.Group || target.ItemType != ForwardItemType.Group) return;
        if (ReferenceEquals(dragged, target)) return;

        int fromIndex = Groups.IndexOf(dragged);
        if (fromIndex < 0) return;
        Groups.RemoveAt(fromIndex);

        int targetIndex = Groups.IndexOf(target);
        if (targetIndex < 0)
        {
            Groups.Insert(fromIndex, dragged);
            return;
        }

        int insertIndex = insertBefore ? targetIndex : targetIndex + 1;
        Groups.Insert(insertIndex, dragged);

        // 把新的分組順序同步回扁平 Configs 清單並寫回 config.txt（否則重開後順序跑掉）
        var newConfigs = new List<ForwardConfig>();
        foreach (var g in Groups)
            foreach (var child in g.Children)
                if (child.Config != null) newConfigs.Add(child.Config);
        Configs = newConfigs;
        SaveConfig();

        RebuildVisibleList();
    }

    private void SaveConfig() => _config.Save(Ssh, Configs);

    private void SetResult(string text, Brush brush)
    {
        ResultText = text;
        ResultBrush = brush;
    }

    /// <summary>視窗關閉時清理：停止計時器並斷開 SSH Master。</summary>
    public void Cleanup()
    {
        _timer.Stop();
        _ssh.StopMaster();
    }

    // ===== Console / SSH 設定 =====

    private TerminalWindow? _terminalWindow;

    private void OpenConsole()
    {
        if (!IsConnected) return;

        // 已有活著的 Terminal → 直接顯示
        if (_terminalWindow != null)
        {
            _terminalWindow.Show();
            _terminalWindow.Activate();
            return;
        }

        var shell = _ssh.CreateShellStream();
        if (shell == null)
        {
            SetResult("Result: ERROR - Failed to create shell.", Brushes.Red);
            return;
        }

        _terminalWindow = new TerminalWindow(shell, _ssh, $"SSH: {Ssh.User}@{Ssh.Host}")
        {
            Owner = Application.Current.MainWindow
        };
        _terminalWindow.Closed += (_, _) => _terminalWindow = null;
        _terminalWindow.Show();
        SetResult("Result: Terminal opened.", Brushes.Green);
    }

    private void OpenSshSettings()
    {
        var dialog = new SshSettingsDialog(Ssh) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            Ssh = dialog.Result;
            _config.Save(Ssh, Configs);
            SetResult("Result: SSH settings saved.", Brushes.Green);
        }
    }

    private void OnSshDisconnected(string? reason = null)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (!IsConnected) return; // 已處理過（防止事件 + 輪詢重複觸發）
            IsConnected = false;
            _connectedAt = null;
            foreach (var group in Groups)
                foreach (var child in group.Children)
                    child.IsActive = false;
            UpdateCounts();
            SetResult(reason != null
                ? $"Result: ERROR - Master disconnected: {reason}"
                : "Result: ERROR - Master disconnected unexpectedly.", Brushes.Red);
        });
    }

    // ===== INotifyPropertyChanged =====

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
