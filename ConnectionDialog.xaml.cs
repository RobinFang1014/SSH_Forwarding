using System.Windows;
using BIOSForwardManager.Models;

namespace BIOSForwardManager;

/// <summary>新增 / 編輯連線的對話框。保存成功時 <see cref="Result"/> 回傳新的 ForwardConfig。</summary>
public partial class ConnectionDialog : Window
{
    public ForwardConfig? Result { get; private set; }

    private readonly List<ForwardConfig> _allConfigs;
    private readonly ForwardConfig? _editing;

    public ConnectionDialog(ForwardConfig? existing, List<ForwardConfig> allConfigs)
    {
        InitializeComponent();
        _allConfigs = allConfigs;
        _editing = existing;
        Title = existing != null ? "Edit Connection" : "Add New Connection";

        if (existing != null)
        {
            TypeBox.Text = existing.Type;
            NameBox.Text = existing.Name;
            LocalPortBox.Text = existing.LocalPort.ToString();
            RemoteIpBox.Text = existing.RemoteIp;
            RemotePortBox.Text = existing.RemotePort.ToString();
            AutoCheck.IsChecked = existing.Auto;
        }
        else
        {
            TypeBox.Text = "http";
            NameBox.Text = "UUT_Node";
            LocalPortBox.Text = "8080";
            RemoteIpBox.Text = "192.168.1.1";
            RemotePortBox.Text = "80";
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TypeBox.Text) || string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show("Type and Name are required.", "Validation Error");
            return;
        }
        if (!int.TryParse(LocalPortBox.Text, out int localPort) ||
            !int.TryParse(RemotePortBox.Text, out int remotePort))
        {
            MessageBox.Show("Ports must be valid numbers.", "Validation Error");
            return;
        }

        // 防呆：檢查是否與現有的 Local Port 衝突（編輯時排除自身）
        bool conflict = _allConfigs.Any(c => c.LocalPort == localPort && !ReferenceEquals(c, _editing));
        if (conflict)
        {
            MessageBox.Show($"Local Port {localPort} already exists in current settings. Please change port.", "Port Conflict");
            return;
        }

        Result = new ForwardConfig
        {
            Type = TypeBox.Text.Trim(),
            Name = NameBox.Text.Trim(),
            LocalPort = localPort,
            RemoteIp = RemoteIpBox.Text.Trim(),
            RemotePort = remotePort,
            Auto = AutoCheck.IsChecked == true
        };
        DialogResult = true;
    }
}
