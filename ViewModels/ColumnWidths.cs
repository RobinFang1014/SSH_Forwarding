using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BIOSForwardManager.ViewModels;

/// <summary>
/// 六個欄位的寬度（像素）。表頭與列項目綁定同一實例，
/// 因此拖曳調整時會同步更新，達到與檔案總管一致的對齊效果。
/// </summary>
public class ColumnWidths : INotifyPropertyChanged
{
    private double _nameWidth = 320;
    private double _typeWidth = 90;
    private double _lPortWidth = 80;
    private double _rPortWidth = 80;
    private double _statusWidth = 100;
    private double _actionWidth = 100;

    public double NameWidth { get => _nameWidth; set => SetClamped(ref _nameWidth, value, 150, 700); }
    public double TypeWidth { get => _typeWidth; set => SetClamped(ref _typeWidth, value, 50, 300); }
    public double LPortWidth { get => _lPortWidth; set => SetClamped(ref _lPortWidth, value, 50, 200); }
    public double RPortWidth { get => _rPortWidth; set => SetClamped(ref _rPortWidth, value, 50, 200); }
    public double StatusWidth { get => _statusWidth; set => SetClamped(ref _statusWidth, value, 60, 300); }
    public double ActionWidth { get => _actionWidth; set => SetClamped(ref _actionWidth, value, 60, 300); }

    private bool SetClamped(ref double field, double value, double min, double max, [CallerMemberName] string? name = null)
    {
        value = Math.Clamp(value, min, max);
        if (Math.Abs(field - value) < 0.01) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
