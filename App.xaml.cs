using System.Windows;
using BIOSForwardManager.Services;

namespace BIOSForwardManager;

/// <summary>Interaction logic for App.xaml</summary>
public partial class App : Application
{
    private TrayHelper? _tray;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // X 關閉視窗 → 結束程式；Minimize → 縮到 tray（OnStateChanged 處理）
        ShutdownMode = ShutdownMode.OnLastWindowClose;

        _mainWindow = new MainWindow();
        _tray = new TrayHelper();
        _tray.Initialize(_mainWindow);
        _mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 確保 SSH master cmd 視窗被關閉（否則 Debug Mode 不會結束）
        if (_mainWindow?.DataContext is ViewModels.MainViewModel vm)
        {
            vm.ShutdownSsh();
        }
        base.OnExit(e);
    }
}
