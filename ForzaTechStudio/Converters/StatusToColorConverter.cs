using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.UI;

namespace ForzaTechStudio.Converters
{
    public class StatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isValid)
            {
                // Green for valid, gray for invalid
                return isValid 
                    ? new SolidColorBrush(Color.FromArgb(255, 16, 185, 129)) // #10B981 Green
                    : new SolidColorBrush(Color.FromArgb(255, 107, 114, 128)); // #6B7280 Gray
            }
            
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
