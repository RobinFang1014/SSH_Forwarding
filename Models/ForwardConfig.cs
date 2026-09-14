namespace BIOSForwardManager.Models;

/// <summary>config.txt 中的 SSH 主連線設定（[SSHConfig] 區段）。</summary>
public class SshConfig
{
    public string User { get; set; } = "";
    public string Host { get; set; } = "";
    public string Port { get; set; } = "";

    /// <summary>SSH 金鑰檔路徑（原生 OpenSSH 認證用，建議未加密金鑰）。</summary>
    public string PrivateKeyPath { get; set; } = "";

    /// <summary>金鑰 passphrase（可選；未加密金鑰留空）。</summary>
    public string Passphrase { get; set; } = "";

    // Legacy：WSL 版使用，原生 OpenSSH 版忽略（保留以相容舊 config.txt）。
    public string Distro { get; set; } = "";
    public string Socket { get; set; } = "";
}

/// <summary>單一端口轉發設定（config.txt 的一行）。</summary>
public class ForwardConfig
{
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public int LocalPort { get; set; }
    public string RemoteIp { get; set; } = "";
    public int RemotePort { get; set; }
    public bool Auto { get; set; }
}
