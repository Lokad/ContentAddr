using System;
using System.ComponentModel;
using System.Globalization;

namespace Lokad.ContentAddr
{
    /// <summary>
    ///     Used to convert <see cref="Hash"/> to and from strings.
    ///     Uses 32-character hexadecimal representation.
    /// </summary>
    /// <remarks>
    ///     As an added bonus, JSON.NET will use this type converter
    ///     to serialize <see cref="Hash"/> as a string.
    /// </remarks>
    public sealed class HashTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(
            ITypeDescriptorContext tdc, 
            Type source) 
        =>
            source == typeof(string);

        public override object ConvertFrom(
            ITypeDescriptorContext tdc, 
            CultureInfo ci, 
            object value)
        {
            var str = (string)value;
            return new Hash(str);
        }

        public override object ConvertTo(
            ITypeDescriptorContext tdc, 
            CultureInfo ci, 
            object value, 
            Type t)
        {
            var hash = (Hash)value;
            if (t == typeof(string))
                return hash.ToString();

            return base.ConvertTo(tdc, ci, value, t);
        }
    }
}
