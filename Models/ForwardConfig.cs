namespace BIOSForwardManager.Models;

/// <summary>config.txt 中的 SSH 主連線設定（[SSHConfig] 區段）。</summary>
public class SshConfig
{
    public string User { get; set; } = "";
    public string Host { get; set; } = "";
    public string Port { get; set; } = "";
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
