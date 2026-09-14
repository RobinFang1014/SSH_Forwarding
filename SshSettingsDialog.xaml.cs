using System.Windows;
using BIOSForwardManager.Models;

namespace BIOSForwardManager;

/// <summary>編輯 SSH 主連線設定（User/Host/Port）。保存成功時 <see cref="Result"/> 回傳新的 SshConfig。</summary>
public partial class SshSettingsDialog : Window
{
    public SshConfig? Result { get; private set; }
    private readonly SshConfig _current;

    public SshSettingsDialog(SshConfig current)
    {
        InitializeComponent();
        _current = current;
        UserBox.Text = current.User;
        HostBox.Text = current.Host;
        PortBox.Text = current.Port;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UserBox.Text) || string.IsNullOrWhiteSpace(HostBox.Text))
        {
            MessageBox.Show("User and Host are required.", "Validation Error");
            return;
        }
        if (!string.IsNullOrWhiteSpace(PortBox.Text) && !int.TryParse(PortBox.Text, out _))
        {
            MessageBox.Show("Port must be a number.", "Validation Error");
            return;
        }
        Result = new SshConfig
        {
            User = UserBox.Text.Trim(),
            Host = HostBox.Text.Trim(),
            Port = PortBox.Text.Trim(),
            PrivateKeyPath = _current.PrivateKeyPath,
            Passphrase = _current.Passphrase,
            Distro = _current.Distro,
            Socket = _current.Socket
        };
        DialogResult = true;
    }
}
