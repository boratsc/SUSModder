using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace SUSModder.Converters
{
    public class CategoryToClassConverter : IValueConverter
    {
        public static readonly CategoryToClassConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string category)
            {
                return category.ToLower() switch
                {
                    "crewmate" => "category-crewmate",
                    "impostor" => "category-impostor",
                    "neutral" => "category-neutral",
                    "modifier" => "category-modifier",
                    _ => "category-neutral"
                };
            }
            return "category-neutral";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
