using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using System;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Concurrent;

namespace SUSModder.Converters
{
    public class AsyncUrlToBitmapConverter : IValueConverter
    {
        public static readonly AsyncUrlToBitmapConverter Instance = new();
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly ConcurrentDictionary<string, Bitmap?> _cache = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string url && !string.IsNullOrWhiteSpace(url))
            {
                // Sprawdź cache
                if (_cache.TryGetValue(url, out var cachedBitmap))
                {
                    return cachedBitmap;
                }

                // Rozpocznij asynchroniczne ładowanie
                _ = LoadImageAsync(url);

                // Zwróć null na razie (pokaże się fallback)
                return null;
            }
            return null;
        }

        private async Task LoadImageAsync(string url)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[AsyncBitmapConverter] Loading image from URL: {url}");

                using var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var memoryStream = new MemoryStream();
                    await stream.CopyToAsync(memoryStream);
                    memoryStream.Position = 0;

                    var bitmap = new Bitmap(memoryStream);
                    _cache[url] = bitmap;

                    System.Diagnostics.Debug.WriteLine($"[AsyncBitmapConverter] Successfully loaded and cached image from URL: {url}");
                }
                else
                {
                    _cache[url] = null;
                    System.Diagnostics.Debug.WriteLine($"[AsyncBitmapConverter] HTTP error {response.StatusCode} for URL: {url}");
                }
            }
            catch (Exception ex)
            {
                _cache[url] = null;
                System.Diagnostics.Debug.WriteLine($"[AsyncBitmapConverter] Exception loading from URL {url}: {ex.Message}");
            }
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
