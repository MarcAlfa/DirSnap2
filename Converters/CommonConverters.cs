using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace DirSnap2.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool b = value is bool flag && flag;
        if (parameter is string str && str.Equals("inverse", StringComparison.OrdinalIgnoreCase))
        {
            b = !b;
        }
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is Visibility vis && vis == Visibility.Visible;
    }
}

public sealed class StringMatchConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        string? valStr = value?.ToString();
        string? paramStr = parameter?.ToString();
        bool match = string.Equals(valStr, paramStr, StringComparison.OrdinalIgnoreCase);
        return match ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
