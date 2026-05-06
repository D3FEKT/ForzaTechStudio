using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace ForzaTechStudio.Converters;

public class BooleanToVisibilityConverter : IValueConverter
{
    public bool Inverse { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool b = false;
        if (value is bool val)
        {
            b = val;
        }

        if (Inverse)
        {
            b = !b;
        }

        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
