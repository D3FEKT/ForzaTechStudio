using Microsoft.UI.Xaml.Data;
using System;

namespace ForzaTechStudio.Converters
{
    public class BoolToVisibilityIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {

            bool isVisible = false;
            
            if (value is bool b) isVisible = b;
            else if (value is null) isVisible = false;
            

            return isVisible ? "\uE7B3" : "\uED1A";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
