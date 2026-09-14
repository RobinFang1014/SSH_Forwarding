# SSH Forward Manager

> 🤖 AI生成的SSH端口轉發管理GUI應用

一個基於 WPF 的 Windows 桌面應用，用於管理和監控 SSH 端口轉發連接。提供友好的圖形界面，支持快速配置、啟動/停止端口轉發，以及實時連接狀態監控。

## ✨ 功能特性

- 🔧 **直觀的配置界面** - 輕鬆添加、編輯和管理 SSH 轉發規則
- 🚀 **快速連接** - 一鍵啟動/停止 SSH 端口轉發
- 📊 **實時監控** - 查看當前活動的轉發連接狀態
- 🔐 **安全認證** - 支持密鑰和密碼認證方式
- 💾 **配置持久化** - 自動保存轉發配置，下次啟動自動恢復
- 🎨 **深色主題** - 適配現代 Windows 11+ UI 風格

## 🛠️ 技術棧

| 項目 | 版本 | 說明 |
|------|------|------|
| **.NET** | 10.0.401 | 最新LTS版本 |
| **框架** | .NET 10.0 Windows Desktop | WPF 應用開發 |
| **UI** | WPF + Windows Forms | XAML 界面設計 |
| **SSH Library** | SSH.NET 2026.0.0 | SSH 連接和端口轉發 |
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

- **SSH.NET** (2026.0.0) - 提供 SSH 協議支持和端口轉發功能

## 🚀 快速開始

### 系統要求

- Windows 10 21H2 或更高版本
- .NET 10.0 Runtime
- 最少 200MB 磁盤空間

### 安裝 .NET Runtime

```powershell
# 檢查已安裝的 .NET 版本
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
├── App.xaml / App.xaml.cs          # 應用程序入口
├── MainWindow.xaml / .cs           # 主窗口
├── Models/                         # 數據模型
│   ├── ForwardConfig.cs            # 轉發配置
│   ├── ForwardItem.cs              # 轉發項目
│   └── TerminalSettings.cs         # 終端設置
├── ViewModels/                     # 視圖模型（MVVM）
├── Services/                       # 業務服務
│   ├── SshService.cs               # SSH 連接管理
│   ├── ConfigService.cs            # 配置管理
│   └── TrayHelper.cs               # 系統托盤
├── Controls/                       # 自定義控件
│   └── TerminalControl.cs          # 終端控件
├── Converters/                     # XAML 值轉換器
│── Dialogs/                        # 對話框
│   ├── LoginDialog.xaml            # 登錄對話框
│   ├── ConnectionDialog.xaml       # 連接配置對話框
│   ├── SshSettingsDialog.xaml      # SSH 設置對話框
│   └── TerminalSettingsDialog.xaml # 終端設置對話框
├── Assets/                         # 資源文件（圖標、圖片等）
└── generate_icons.ps1              # 圖標生成腳本
```

## 🔧 配置文件

### global.json
指定項目使用的 .NET SDK 版本：
```json
{
  "sdk": {
    "version": "10.0.401",
    "rollForward": "latestFeature"
  }
}
```

## 📝 使用示例

### 添加新的 SSH 轉發配置

1. 打開應用程序
2. 點擊「新增連接」
3. 填入 SSH 服務器信息
4. 配置本地轉發端口和遠程端口
5. 保存配置

### 啟動端口轉發

1. 從列表中選擇已配置的轉發
2. 點擊「連接」按鈕
3. 應用程序會建立 SSH 連接並啟動端口轉發

## 🐛 故障排除

### 無法連接到 SSH 服務器
- 確認服務器地址和端口是否正確
- 檢查網絡連接
- 驗證 SSH 認證信息（用戶名、密碼或密鑰）

### 端口轉發失敗
- 確認本地端口未被占用
- 檢查遠程服務器端口是否可訪問
- 查看應用日誌獲取詳細錯誤信息

## 🤝 貢獻

這是一個 AI 輔助開發的項目，歡迎提出改進建議或提交 Pull Request。

## 📄 許可證

MIT License - 詳見 [LICENSE](LICENSE) 文件

## 📞 支持

如有問題或建議，歡迎通過 GitHub Issues 反饋。

---

**最後更新**: 2026年9月14日  
**.NET 版本**: 10.0.401  
**應用版本**: 1.0.0
