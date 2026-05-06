using Microsoft.UI.Xaml.Data;
using System;

namespace ForzaTechStudio.Converters;

public class BooleanNegationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b)
        {
            return !b;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b)
        {
            return !b;
        }
        return false;
    }
}
