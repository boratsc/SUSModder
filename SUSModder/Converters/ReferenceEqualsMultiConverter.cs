using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SUSModder.Converters
{
    public sealed class ReferenceEqualsMultiConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count < 2)
            {
                return false;
            }

            bool equals = ReferenceEquals(values[0], values[1]);
            // "Invert" = czy to INNY element (np. wiersz nie jest zaznaczonym modem).
            return parameter is string param && (param == "Invert" || param == "True") ? !equals : equals;
        }
    }
}
