using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BIOSForwardManager.Models;

namespace BIOSForwardManager.Services;

/// <summary>
/// 讀寫 config.txt（與 PowerShell 版格式完全相容）。
/// 預設從 exe 目錄往上搜尋 config.txt，找不到則使用 exe 目錄下的路徑。
/// </summary>
public class ConfigService
{
    public string ConfigPath { get; }

    public ConfigService()
    {
        ConfigPath = FindConfigFile();
    }

    /// <summary>從 exe 目錄逐層往上搜尋 config.txt（開發時 exe 在 bin/... 下，可找到專案根目錄的設定檔）。</summary>
    private static string FindConfigFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "config.txt");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "config.txt");
    }

    /// <summary>讀取設定檔，回傳 (SSH 主連線設定, 轉發清單)。</summary>
    public (SshConfig Ssh, List<ForwardConfig> Configs) Load()
    {
        var ssh = new SshConfig();
        var configs = new List<ForwardConfig>();
        if (!File.Exists(ConfigPath)) return (ssh, configs);

        bool inSsh = false;
        foreach (var raw in File.ReadAllLines(ConfigPath))
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("[SSHConfig]")) { inSsh = true; continue; }

            if (inSsh)
            {
                if (TryMatch(line, @"^User\s*=\s*(.+)$", out var m)) { ssh.User = m.Groups[1].Value.Trim(); continue; }
                if (TryMatch(line, @"^Host\s*=\s*(.+)$", out m)) { ssh.Host = m.Groups[1].Value.Trim(); continue; }
                if (TryMatch(line, @"^Port\s*=\s*(\d+)$", out m)) { ssh.Port = m.Groups[1].Value.Trim(); continue; }
                if (TryMatch(line, @"^Distro\s*=\s*(.+)$", out m)) { ssh.Distro = m.Groups[1].Value.Trim(); continue; }
                if (TryMatch(line, @"^Socket\s*=\s*(.+)$", out m)) { ssh.Socket = m.Groups[1].Value.Trim(); continue; }
                if (TryMatch(line, @"^PrivateKeyPath\s*=\s*(.+)$", out m)) { ssh.PrivateKeyPath = m.Groups[1].Value.Trim(); continue; }
                if (TryMatch(line, @"^Passphrase\s*=\s*(.*)$", out m)) { ssh.Passphrase = m.Groups[1].Value.Trim(); continue; }
                if (line.StartsWith("["))
                {
                    inSsh = false; // 進入新 section — 這行本身是 forward 條目，fall through 到下方解析
                }
                else
                {
                    continue; // SSHConfig 段內的未知列
                }
            }

            // [Type] Name|LocalPort:RemoteIP:RemotePort[|AUTO]
            var fm = Regex.Match(line,
                @"^\[(.*?)\]\s*(.*?)\s*\|\s*(\d+)\s*:\s*([\d\.]+)\s*:\s*(\d+)(?:\s*\|\s*(AUTO))?");
            if (fm.Success)
            {
                configs.Add(new ForwardConfig
                {
                    Type = fm.Groups[1].Value.Trim(),
                    Name = fm.Groups[2].Value.Trim(),
                    LocalPort = int.Parse(fm.Groups[3].Value),
                    RemoteIp = fm.Groups[4].Value.Trim(),
                    RemotePort = int.Parse(fm.Groups[5].Value),
                    Auto = fm.Groups[6].Success && fm.Groups[6].Value == "AUTO"
                });
            }
        }
        return (ssh, configs);
    }

    /// <summary>將設定寫回 config.txt（UTF-8）。</summary>
    public void Save(SshConfig ssh, List<ForwardConfig> configs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[SSHConfig]");
        sb.AppendLine($"User={ssh.User}");
        sb.AppendLine($"Host={ssh.Host}");
        if (!string.IsNullOrEmpty(ssh.Port)) sb.AppendLine($"Port={ssh.Port}");
        if (!string.IsNullOrEmpty(ssh.Distro)) sb.AppendLine($"Distro={ssh.Distro}");
        if (!string.IsNullOrEmpty(ssh.Socket)) sb.AppendLine($"Socket={ssh.Socket}");
        if (!string.IsNullOrEmpty(ssh.PrivateKeyPath)) sb.AppendLine($"PrivateKeyPath={ssh.PrivateKeyPath}");
        if (!string.IsNullOrEmpty(ssh.Passphrase)) sb.AppendLine($"Passphrase={ssh.Passphrase}");
        sb.AppendLine();
        foreach (var c in configs)
        {
            string auto = c.Auto ? "|AUTO" : "";
            sb.AppendLine($"[{c.Type}] {c.Name}|{c.LocalPort}:{c.RemoteIp}:{c.RemotePort}{auto}");
        }
        File.WriteAllText(ConfigPath, sb.ToString(), new UTF8Encoding(false));
    }

    private static bool TryMatch(string line, string pattern, out Match m)
    {
        m = Regex.Match(line, pattern);
        return m.Success;
    }
}
