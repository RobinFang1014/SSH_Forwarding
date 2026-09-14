using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BIOSForwardManager.Converters;

/// <summary>將層級（int）轉換為名稱欄的左縮排 Thickness。</summary>
public class LevelToMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int level = value is int i ? i : 0;
        double indent = level * 20.0;
        return new Thickness(indent, 0, 0, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool → Visibility。</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}
