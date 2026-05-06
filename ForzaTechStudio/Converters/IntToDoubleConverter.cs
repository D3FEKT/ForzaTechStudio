using System;
using Microsoft.UI.Xaml.Data;

namespace ForzaTechStudio.Converters
{
    public class IntToDoubleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is long l)    return (double)l;
            if (value is int i)     return (double)i;
            if (value is uint u)    return (double)u;
            if (value is short s)   return (double)s;
            if (value is ushort us) return (double)us;
            if (value is sbyte sb)  return (double)sb;
            if (value is byte b)    return (double)b;
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is double d)
            {
                if (targetType == typeof(long))   return (long)d;
                if (targetType == typeof(uint))   return (uint)Math.Max(0, d);
                if (targetType == typeof(short))  return (short)Math.Clamp((int)d, short.MinValue, short.MaxValue);
                if (targetType == typeof(ushort)) return (ushort)Math.Clamp((int)d, ushort.MinValue, ushort.MaxValue);
                if (targetType == typeof(sbyte))  return (sbyte)Math.Clamp((int)d, sbyte.MinValue, sbyte.MaxValue);
                if (targetType == typeof(byte))   return (byte)Math.Clamp((int)d, byte.MinValue, byte.MaxValue);
                return (int)d;
            }
            if (targetType == typeof(long)) return 0L;
            return 0;
        }
    }
}
