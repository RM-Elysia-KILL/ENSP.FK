using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ENSP.ZD.Helpers;

internal class StringEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string strValue && parameter is string strParam)
            return strValue == strParam ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
