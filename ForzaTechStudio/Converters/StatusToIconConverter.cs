using Microsoft.UI.Xaml.Data;
using System;

namespace ForzaTechStudio.Converters
{
    public class StatusToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isValid)
            {
                // Checkmark for valid, X for invalid
                return isValid ? "\uE73E" : "\uE711"; // ? or ?
            }
            
            return "\uE711"; // Default to X
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
