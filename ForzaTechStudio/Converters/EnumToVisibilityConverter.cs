using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace ForzaTechStudio.Converters
{
    public class EnumToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value == null || parameter == null)
                return Visibility.Collapsed;

            string? enumValue = value.ToString();
            string? targetValue = parameter.ToString();

            return (enumValue?.Equals(targetValue, StringComparison.OrdinalIgnoreCase) == true) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
