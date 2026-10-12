using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LogiWheelForge.Controls;

public sealed class BooleanVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true ^ Invert) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        ((Visibility)value == Visibility.Visible) ^ Invert;
}
