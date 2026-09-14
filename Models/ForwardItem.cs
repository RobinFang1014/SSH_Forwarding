using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BIOSForwardManager.Models;

/// <summary>TreeListView 項目類型。</summary>
public enum ForwardItemType
{
    Group,      // 父節點：Remote IP 分組
    Connection  // 子節點：單一端口轉發
}

/// <summary>
/// TreeListView 的顯示項目。Group 為 IP 分組（父），Connection 為單一轉發（子）。
/// 實作 INotifyPropertyChanged，讓 WPF 資料綁定在資料變更時自動刷新 UI（無需手動 Refresh 按鈕）。
/// </summary>
public class ForwardItem : INotifyPropertyChanged
{
    public ForwardItemType ItemType { get; set; }
    public string Name { get; set; } = "";
    public int Level { get; set; }

    // Group 專用
    public string Ip { get; set; } = "";

    // Connection 專用
    public string Type { get; set; } = "";
    public int LocalPort { get; set; }
    public int RemotePort { get; set; }
    public bool Auto { get; set; }

    /// <summary>對應的原始設定（供 SSH 指令使用）。</summary>
    public ForwardConfig? Config { get; set; }

    /// <summary>父節點（連線 → 其 IP 分組）。</summary>
    public ForwardItem? Parent { get; set; }

    public ObservableCollection<ForwardItem> Children { get; } = new();

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool HasChildren => Children.Count > 0;
    public bool IsConnection => ItemType == ForwardItemType.Connection;

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(ActionText));
                OnPropertyChanged(nameof(DisplayStatus));
            }
        }
    }

    public string StatusText => IsActive ? "ACTIVE" : "INACTIVE";
    public string ActionText => IsActive ? "Disable" : "Enable";

    /// <summary>狀態欄顯示：連線顯示 ACTIVE/INACTIVE，分組顯示 active/total。</summary>
    public string DisplayStatus =>
        ItemType == ForwardItemType.Group ? GroupStatusText : StatusText;

    /// <summary>分組的 active/total 摘要。</summary>
    public string GroupStatusText
    {
        get
        {
            int total = Children.Count;
            int active = Children.Count(c => c.IsActive);
            return $"{active}/{total}";
        }
    }

    /// <summary>顯示名稱（AUTO 連線加 [AUTO] 標記）。</summary>
    public string DisplayName =>
        ItemType == ForwardItemType.Connection && Auto ? $"{Name} [AUTO]" : Name;

    /// <summary>各欄顯示文字（分組節點留空，避免顯示 0）。</summary>
    public string TypeText => IsConnection ? Type : "";
    public string LocalPortText => IsConnection ? LocalPort.ToString() : "";
    public string RemotePortText => IsConnection ? RemotePort.ToString() : "";

    /// <summary>項目圖示（emoji，免 PNG 資源）。</summary>
    public string IconText => ItemType switch
    {
        ForwardItemType.Group => "📁",
        _ => Type.ToUpperInvariant() switch
        {
            "HTTP" => "🌐",
            "SSH" => "🔒",
            "RDP" => "🖥️",
            "GDB" => "🐞",
            "CSCRIPT" => "📜",
            _ => "🔗"
        }
    };

    /// <summary>項目圖示（彩色 PNG，從組件嵌入資源載入並快取）。</summary>
    public ImageSource Icon => ItemType switch
    {
        ForwardItemType.Group => LoadIcon("folder.png"),
        _ => Type.ToUpperInvariant() switch
        {
            "HTTP" => LoadIcon("globe.png"),
            "SSH" => LoadIcon("lock.png"),
            "RDP" => LoadIcon("monitor.png"),
            "GDB" => LoadIcon("bug.png"),
            "CSCRIPT" => LoadIcon("scroll.png"),
            _ => LoadIcon("link.png")
        }
    };

    private static readonly Dictionary<string, ImageSource> _iconCache = new();

    private static ImageSource LoadIcon(string filename)
    {
        if (_iconCache.TryGetValue(filename, out var cached)) return cached;
        var img = new BitmapImage();
        img.BeginInit();
        img.UriSource = new Uri($"pack://application:,,,/Assets/{filename}", UriKind.Absolute);
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.EndInit();
        img.Freeze();
        _iconCache[filename] = img;
        return img;
    }

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

    /// <summary>供 ViewModel 在子項目狀態變更後，刷新分組摘要。</summary>
    public void NotifyGroupStatusChanged() => OnPropertyChanged(nameof(DisplayStatus));
}
