using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace ForzaTechStudio.Converters
{
    public class IntToVisibilityConverter : IValueConverter
    {
        public bool Inverse { get; set; }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            int val = 0;
            if (value is int i)
            {
                val = i;
            }

            bool b = val > 0;

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
