using System;
using Microsoft.UI.Xaml.Data;
using ForzaTechStudio.Services;
using System.Globalization;

namespace ForzaTechStudio.Converters
{
    public class NameHashConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is uint hash)
            {
                var name = NameHashService.Instance.GetName(hash);
                if (!string.IsNullOrEmpty(name))
                {
                    return $"{name} (0x{hash:X8})";
                }
                return $"0x{hash:X8}";
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is string str)
            {
                // Try to parse friendly name first
                var hash = NameHashService.Instance.GetHash(str);
                if (hash.HasValue) return hash.Value;

                // Try parse hex
                if (str.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    if (uint.TryParse(str.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out var result))
                        return result;
                }
            }
            throw new NotImplementedException();
        }
    }
}
