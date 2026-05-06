using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace ForzaTechStudio.Converters
{
    public class NotNullToVisibilityConverter : IValueConverter
    {
        public bool Inverse { get; set; }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool b = value != null;

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
}
