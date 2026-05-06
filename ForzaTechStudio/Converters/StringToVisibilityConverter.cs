using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace ForzaTechStudio.Views
{
    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var str = value as string;
            return !string.IsNullOrEmpty(str) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
