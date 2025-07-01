using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Globalization;

namespace SUSModder.Converters
{
    public class StringToBitmapConverter : IValueConverter
    {
        public static readonly StringToBitmapConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string fileName && !string.IsNullOrWhiteSpace(fileName))
            {
                try
                {
                    var uri = new Uri($"avares://SUSModder/Assets/{fileName}");
                    var asset = AssetLoader.Open(uri);
                    return new Bitmap(asset);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[BitmapConverter] ERROR loading {fileName}: {ex.Message}");
                    return null;
                }
            }
            return null;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}