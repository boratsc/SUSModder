using Avalonia.Data.Converters;
using System;
using System.Globalization;
using System.IO;

namespace SUSModder.Converters
{
    public class PathShorteningConverter : IValueConverter
    {
        public static readonly PathShorteningConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string path && !string.IsNullOrWhiteSpace(path))
            {
                int maxLength = 40; // Domyślna długość

                if (parameter is string paramStr && int.TryParse(paramStr, out int paramLength))
                {
                    maxLength = paramLength;
                }

                if (path.Length <= maxLength)
                    return path;

                // Skróć ścieżkę inteligentnie
                try
                {
                    var directory = Path.GetDirectoryName(path);
                    var fileName = Path.GetFileName(path);

                    if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
                        return path.Length > maxLength ? path.Substring(0, maxLength - 3) + "..." : path;

                    var availableLength = maxLength - fileName.Length - 4; // 4 dla "...\"

                    if (availableLength > 0 && directory.Length > availableLength)
                    {
                        return directory.Substring(0, availableLength) + "...\\" + fileName;
                    }
                    else if (availableLength <= 0)
                    {
                        return "..." + fileName;
                    }

                    return path;
                }
                catch
                {
                    return path.Length > maxLength ? path.Substring(0, maxLength - 3) + "..." : path;
                }
            }
            return value;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
