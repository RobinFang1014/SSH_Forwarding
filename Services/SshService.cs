using System.Net;
using System.Net.Sockets;
using Renci.SshNet;
using BIOSForwardManager.Models;

namespace BIOSForwardManager.Services;

/// <summary>
/// 使用 SSH.NET 直接連線 Jump Server 並管理 port forwarding。
/// 不需要 ssh.exe、TTY、ConPTY 或 cmd 視窗。
/// 認證：keyboard-interactive (password + 2FA)。
/// </summary>
public class SshService : IDisposable
{
    private SshClient? _client;
    private readonly Dictionary<int, ForwardedPortLocal> _forwards = new();
    private readonly object _lock = new();

    public bool IsConnected => _client?.IsConnected == true;
    public event Action<string>? Log;

    /// <summary>
    /// 連線「非預期」斷線時觸發（keep-alive 失敗 / 網路錯誤 / server 端關閉 / 操作失敗發現斷線）。
    /// 參數為原因。手動 Disconnect() 不會觸發。
    /// 注意：可能從背景執行緒觸發，訂閱者需自行 Dispatcher 回 UI 執行緒。
    /// </summary>
    public event Action<string?>? Disconnected;

    private int _disconnectFired;

    private void RaiseLog(string msg) => Log?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");

    // 注意：SSH.NET 2026.0.0 的 ErrorOccurred 參數型別是 Renci.SshNet.Common.ExceptionEventArgs
    //（舊版的 SshClientExceptionEventArgs 已改名）
    private void OnClientErrorOccurred(object? sender, Renci.SshNet.Common.ExceptionEventArgs e)
    {
        RaiseLog($"Connection error: {e.Exception.Message}");
        RaiseDisconnected(e.Exception.Message);
    }

    /// <summary>觸發 Disconnected 事件（每次連線只觸發一次，避免事件+輪詢重複處理）。</summary>
    private void RaiseDisconnected(string? reason)
    {
        if (Interlocked.Exchange(ref _disconnectFired, 1) == 1) return;
        Disconnected?.Invoke(reason);
    }

    public (bool Success, string Error) Connect(SshConfig ssh, string password, string totpCode)
    {
        if (IsConnected) return (true, "Already connected.");

        try
        {
            // 斷線後重連：清理殘留的舊 client、重置「已觸發」旗標
            if (_client != null) { try { _client.Dispose(); } catch { } _client = null; }
            Interlocked.Exchange(ref _disconnectFired, 0);

            int port = int.TryParse(ssh.Port, out int p) ? p : 22;

            var connectionInfo = new KeyboardInteractiveConnectionInfo(ssh.Host, port, ssh.User);

            // 處理 server 的 prompt（password / 2FA）
            connectionInfo.AuthenticationPrompt += (sender, e) =>
            {
                foreach (var prompt in e.Prompts)
                {
                    string pl = (prompt.Request ?? "").ToLower();
                    prompt.Response = (pl.Contains("password") || pl.Contains("passphrase")) ? password : totpCode;
                }
            };

            _client = new SshClient(connectionInfo);

            // Keep-alive：等同 OpenSSH 的 ServerAliveInterval 60。
            // 防止 NAT/防火牆把閒置連線砍掉導致 port forward 靜默失效。
            // （ServerAliveCountMax 無對應 — SSH.NET keep-alive 失敗即判定斷線）
            // 注意：SSH.NET 2026.0.0 起此屬性在 BaseClient（SshClient）上，不在 ConnectionInfo。
            _client.KeepAliveInterval = TimeSpan.FromSeconds(60);

            // 非預期斷線偵測：SSH.NET 偵測到斷線（keep-alive 失敗/網路錯誤/server 關閉）
            // 會觸發 ErrorOccurred → 通知 UI 立即切換為「已斷線」（不用等 5 秒輪詢）
            _client.ErrorOccurred += OnClientErrorOccurred;

            RaiseLog($"Connecting to {ssh.User}@{ssh.Host}:{port}...");
            _client.Connect();

            RaiseLog($"Connected to {ssh.User}@{ssh.Host}");
            return (true, "");
        }
        catch (Exception ex)
        {
            RaiseLog($"Connection ERROR: {ex.Message}");
            try { _client?.Dispose(); } catch { }
            _client = null;
            return (false, ex.Message);
        }
    }

    public (bool Success, string Output) EnableForward(SshConfig ssh, ForwardConfig cfg)
    {
        if (!IsConnected || _client == null) return (false, "Not connected");

        try
        {
            lock (_lock)
            {
                if (_forwards.TryGetValue(cfg.LocalPort, out var existing) && existing.IsStarted)
                    return (true, "Already active");

                var forward = new ForwardedPortLocal(
                    "127.0.0.1", (uint)cfg.LocalPort, cfg.RemoteIp, (uint)cfg.RemotePort);
                _client.AddForwardedPort(forward);
                forward.Start();
                _forwards[cfg.LocalPort] = forward;
                RaiseLog($"Enabled {cfg.Name}: 127.0.0.1:{cfg.LocalPort} -> {cfg.RemoteIp}:{cfg.RemotePort}");
                return (true, "");
            }
        }
        catch (Exception ex)
        {
            RaiseLog($"FAILED enable {cfg.Name}: {ex.Message}");
            if (!IsConnected) RaiseDisconnected(ex.Message);
            return (false, ex.Message);
        }
    }

    public (bool Success, string Output) DisableForward(SshConfig ssh, ForwardConfig cfg)
    {
        if (!IsConnected || _client == null) return (false, "Not connected");

        try
        {
            lock (_lock)
            {
                if (_forwards.TryGetValue(cfg.LocalPort, out var forward))
                {
                    forward.Stop();
                    _client.RemoveForwardedPort(forward);
                    _forwards.Remove(cfg.LocalPort);
                    RaiseLog($"Disabled {cfg.Name}: 127.0.0.1:{cfg.LocalPort}");
                    return (true, "");
                }
                return (false, "Forward not found");
            }
        }
        catch (Exception ex)
        {
            if (!IsConnected) RaiseDisconnected(ex.Message);
            return (false, ex.Message);
        }
    }

    public bool IsPortInUse(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return false;
        }
        catch { return true; }
    }

    public void Disconnect()
    {
        lock (_lock)
        {
            // 手動斷線：先標記「已觸發」，避免 ErrorOccurred 誤觸發 Disconnected 事件
            Interlocked.Exchange(ref _disconnectFired, 1);
            foreach (var f in _forwards.Values) { try { f.Stop(); } catch { } }
            _forwards.Clear();
            if (_client != null)
            {
                try { _client.ErrorOccurred -= OnClientErrorOccurred; } catch { }
                try { _client.Disconnect(); } catch { }
                try { _client.Dispose(); } catch { }
                _client = null;
            }
            RaiseLog("Disconnected.");
        }
    }

    public void Cleanup() => Disconnect();

    /// <summary>建立互動 shell（用於 Terminal 視窗下指令）。</summary>
    public ShellStream? CreateShellStream()
    {
        if (!IsConnected || _client == null) return null;
        // TERM 必須用 "xterm-256color"（不是 "xterm"）：
        // Ubuntu 預設 .bashrc 只在 $TERM 匹配 xterm-color|xterm-256color|putty|... 時才啟用 color_prompt，
        // 純 "xterm" 不在清單內 → bash 用無色 prompt（u@h:~$ 全白）。
        // 用 xterm-256color 可讓遠端送出彩色 prompt（綠色 user@host、藍色路徑）。
        try
        {
            return _client.CreateShellStream("xterm-256color", 80, 24, 800, 600, 4096);
        }
        catch (Exception ex)
        {
            RaiseLog($"CreateShellStream failed: {ex.Message}");
            if (!IsConnected) RaiseDisconnected(ex.Message);
            return null;
        }
    }

    /// <summary>
    /// 送 SIGWINCH（window-change）通知遠端 PTY 尺寸變更。
    /// SSH.NET 的 ShellStream 未公開 channel，故用 reflection 取內部 _channel 並呼叫 SendWindowChangeRequest。
    /// 失敗時靜默忽略（終端機仍可運作，只是遠端尺寸不更新）。
    /// </summary>
    public void SendWindowChange(ShellStream shell, uint cols, uint rows, uint widthPx, uint heightPx)
    {
        try
        {
            var field = typeof(ShellStream).GetField("_channel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var channel = field?.GetValue(shell);
            if (channel == null) return;
            var method = channel.GetType().GetMethod("SendWindowChangeRequest",
                new[] { typeof(uint), typeof(uint), typeof(uint), typeof(uint) });
            method?.Invoke(channel, new object[] { cols, rows, widthPx, heightPx });
        }
        catch { /* 忽略：SIGWINCH 失敗不影響終端機 */ }
    }

    // ===== Compat (old API) =====
    public bool IsMasterReady() => IsConnected;
    public void StopMaster() => Disconnect();
    public bool IsForwardActive(int port)
    {
        lock (_lock) return _forwards.TryGetValue(port, out var f) && f.IsStarted;
    }

    public void Dispose() => Disconnect();
}
