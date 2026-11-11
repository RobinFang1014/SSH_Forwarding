# SSH Forward Manager

> 🤖 AI 輔助開發的 SSH 端口轉發管理 GUI 應用

一個基於 WPF 的 Windows 桌面應用，透過 Jump Server 管理 SSH 本地端口轉發（Local Port Forwarding）。提供圖形介面，可快速設定、啟動/停止轉發，並即時顯示連線狀態。

## ✨ 功能特性

- 🔐 **密碼 + 二階段認證（2FA）** - 以 keyboard-interactive 方式登入 Jump Server
- 🔧 **轉發規則管理** - 新增、編輯、移除轉發規則，依 Remote IP 自動分組
- 🚀 **一鍵啟動** - 單筆 Enable/Disable，或以 Start Auto 啟動所有標記為 AUTO 的規則
- 📊 **狀態監控** - 每 5 秒刷新 ACTIVE/INACTIVE 狀態與連線時間；斷線即時通知
- 🖥 **內建 Terminal** - 直接在 Jump Server 上開啟互動式 Shell（可自訂字型、顏色）
- 💾 **設定持久化** - 規則與 SSH 設定存於 `config.txt`，下次啟動自動載入
- 📌 **系統匣** - 最小化時縮到系統匣，圖示顯示連線狀態

## 🛠️ 技術棧

| 項目 | 版本 | 說明 |
|------|------|------|
| **.NET SDK** | 10.0.401 | 由 `global.json` 指定 |
| **框架** | .NET 10.0 Windows Desktop | WPF 應用開發 |
| **UI** | WPF + Windows Forms | WPF 介面；Windows Forms 僅用於系統匣圖示 |
| **SSH Library** | SSH.NET 2026.0.0 | SSH 連線與端口轉發（不需 ssh.exe） |
| **IDE** | Visual Studio 2022+ | C# 開發環境 |

### 專案配置

```xml
<PropertyGroup>
  <TargetFramework>net10.0-windows</TargetFramework>
  <UseWPF>true</UseWPF>
  <UseWindowsForms>true</UseWindowsForms>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
</PropertyGroup>
```

## 📦 依賴套件

- **SSH.NET** (2026.0.0) - 提供 SSH 協定支援與端口轉發功能

## 🚀 快速開始

### 系統要求

- Windows 10 / 11
- .NET 10.0 Desktop Runtime（開發需 .NET 10 SDK）

### 檢查 .NET 版本

```powershell
dotnet --list-sdks
dotnet --list-runtimes
```

### 編譯項目

```bash
# 還原 NuGet 包
dotnet restore

# 編譯
dotnet build

# 發佈 Release 版本
dotnet publish -c Release
```

### 運行應用

```bash
# 直接運行
dotnet run

# 或執行已編譯的 exe
./bin/Debug/net10.0-windows/BIOSForwardManager.exe
```

## 📁 項目結構

```
├── App.xaml / App.xaml.cs                  # 應用程式入口
├── MainWindow.xaml / .cs                   # 主視窗（工具列、轉發列表、狀態列）
├── LoginDialog.xaml / .cs                  # 登入對話框（密碼 + 2FA）
├── ConnectionDialog.xaml / .cs             # 新增/編輯轉發規則
├── SshSettingsDialog.xaml / .cs            # Jump Server 設定（User/Host/Port）
├── TerminalWindow.xaml / .cs               # SSH Terminal 視窗
├── TerminalSettingsDialog.xaml / .cs       # Terminal 字型/顏色設定
├── Models/
│   ├── ForwardConfig.cs                    # SshConfig、ForwardConfig 設定模型
│   ├── ForwardItem.cs                      # 列表顯示項目（IP 分組 / 單筆轉發）
│   └── TerminalSettings.cs                 # Terminal 設定（存於 terminal_settings.json）
├── ViewModels/
│   ├── MainViewModel.cs                    # 主視窗邏輯（MVVM）
│   ├── RelayCommand.cs
│   └── ColumnWidths.cs
├── Services/
│   ├── SshService.cs                       # SSH 連線、轉發管理
│   ├── ConfigService.cs                    # config.txt 讀寫
│   └── TrayHelper.cs                       # 系統匣
├── Controls/
│   └── TerminalControl.cs                  # Terminal 繪製控制項
├── Terminal/                               # VT/ANSI 解析與畫面緩衝
│   ├── TerminalParser.cs
│   ├── TerminalBuffer.cs
│   └── TerminalCell.cs
├── Converters/                             # XAML 值轉換器
├── Assets/                                 # 圖示 PNG
└── generate_icons.ps1                      # 圖示產生腳本
```

## 🔧 設定檔

### config.txt

程式啟動時會從 exe 所在目錄**逐層往上**搜尋 `config.txt`（開發時可直接放在專案根目錄）；找不到時會在 exe 目錄建立。格式如下：

```ini
[SSHConfig]
User=your_account
Host=jump.example.com
Port=22

[http] UUT_Node|8080:192.168.1.1:80|AUTO
[ssh] UUT_Shell|2222:192.168.1.1:22
```

轉發規則格式：`[類型] 名稱|本地Port:遠端IP:遠端Port[|AUTO]`

- **類型**：自由填寫，`http` / `ssh` / `rdp` / `gdb` / `cscript` 會顯示對應圖示
- **遠端 IP**：目前僅支援 IPv4 位址
- **AUTO**：連線 Jump Server 後自動啟動此轉發
- 本地端口只綁定 `127.0.0.1`

> ⚠️ `config.txt` 含帳號與主機資訊，請勿提交到版本控制。

### global.json

指定專案使用的 .NET SDK 版本：

```json
{
  "sdk": {
    "version": "10.0.401",
    "rollForward": "latestFeature"
  }
}
```

## 📝 使用說明

### 1. 設定 Jump Server

點擊工具列「⚙ SSH 設定」，填入 User、Host、Port（預設 22），儲存。

### 2. 連線 Jump Server

1. 點擊「Connect Master」
2. 在登入視窗輸入**密碼**與 **2FA 驗證碼**（兩者皆必填）
3. 連線成功後，狀態列顯示綠燈，並自動啟動所有 AUTO 規則

### 3. 新增轉發規則

點擊「Add」，填入類型、名稱、本地端口、遠端 IP、遠端端口，可勾選「Auto Start on Master Connect」。

### 4. 啟動 / 停止轉發

- 在列表中點擊該筆的「Enable / Disable」按鈕
- 「Start Auto」：啟動所有 AUTO 規則
- 「Clear All」：停止所有已啟動的轉發

啟動後即可透過 `127.0.0.1:本地端口` 存取遠端服務。

### 5. 中斷連線

點擊「Exit Master」會停止所有轉發並中斷 SSH 連線。

## 🔐 認證方式

僅支援 **密碼 + 2FA**（SSH keyboard-interactive）：

- Server 提示文字含 `password` 或 `passphrase` 時，送出密碼
- 其他提示一律送出 2FA 驗證碼

不支援 SSH 金鑰登入。斷線後需重新輸入密碼與新的 2FA 驗證碼。

## 🐛 故障排除

### 無法連線到 Jump Server
- 確認 Host / Port / User 是否正確
- 確認 2FA 驗證碼未過期（請在驗證碼剛更新時輸入）
- 確認 Jump Server 允許 keyboard-interactive 認證
- 錯誤訊息會顯示在下方狀態列

### 端口轉發失敗
- 確認本地端口未被其他程式占用
- 確認遠端 IP / Port 從 Jump Server 可連通（可用內建 Terminal 測試）

### 連線突然中斷
- 程式每 60 秒送出 keep-alive；若網路中斷或 Server 關閉連線，狀態列會顯示紅色錯誤訊息，需重新連線

## 🤝 貢獻

這是一個 AI 輔助開發的項目，歡迎提出改進建議或提交 Pull Request。

## 📄 許可證

MIT License - 詳見 [LICENSE](LICENSE) 文件

---

**最後更新**: 2026年11月12日  
**.NET 版本**: 10.0.401  
**應用版本**: 1.0.0
