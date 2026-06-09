using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace SUSModder.Services
{
    /// <summary>
    /// Serwis do preloadowania i cachowania ikon modów (lokalne avares + CDN HTTP).
    /// </summary>
    public class ModIconPreloader
    {
        private static readonly ConcurrentDictionary<string, Bitmap?> _cachedIcons = new();
        private static readonly object _lockObject = new();
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };
        private static bool _isPreloading = false;

        public static Bitmap? GetIcon(string? iconReference)
        {
            if (string.IsNullOrWhiteSpace(iconReference))
                return null;

            if (_cachedIcons.TryGetValue(iconReference, out var cachedBitmap))
                return cachedBitmap;

            return LoadIconSync(iconReference);
        }

        public static async Task PreloadIconsAsync(IEnumerable<string?> iconReferences)
        {
            lock (_lockObject)
            {
                if (_isPreloading)
                    return;
                _isPreloading = true;
            }

            try
            {
                var tasks = new List<Task>();

                foreach (var iconReference in iconReferences)
                {
                    if (string.IsNullOrWhiteSpace(iconReference))
                        continue;

                    if (_cachedIcons.ContainsKey(iconReference))
                        continue;

                    tasks.Add(Task.Run(() => LoadIconSync(iconReference)));
                }

                await Task.WhenAll(tasks);
                System.Diagnostics.Debug.WriteLine($"[ModIconPreloader] Preloaded {tasks.Count} icons");
            }
            finally
            {
                lock (_lockObject)
                {
                    _isPreloading = false;
                }
            }
        }

        private static Bitmap? LoadIconSync(string iconReference)
        {
            if (_cachedIcons.TryGetValue(iconReference, out var cached))
                return cached;

            try
            {
                Bitmap bitmap;
                if (TryLoadBundledAsset(iconReference, out var bundledBitmap))
                {
                    _cachedIcons.TryAdd(iconReference, bundledBitmap);
                    return bundledBitmap;
                }

                if (IsRemoteUrl(iconReference))
                {
                    using var stream = _httpClient.GetStreamAsync(iconReference).GetAwaiter().GetResult();
                    using var memory = new MemoryStream();
                    stream.CopyTo(memory);
                    memory.Position = 0;
                    bitmap = new Bitmap(memory);
                }
                else
                {
                    var fileName = iconReference.Replace('\\', '/');
                    var lastSlash = fileName.LastIndexOf('/');
                    if (lastSlash >= 0)
                        fileName = fileName[(lastSlash + 1)..];

                    var uri = new Uri($"avares://SUSModder/Assets/{fileName}");
                    using var asset = AssetLoader.Open(uri);
                    bitmap = new Bitmap(asset);
                }

                _cachedIcons.TryAdd(iconReference, bitmap);
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ModIconPreloader] ERROR loading {iconReference}: {ex.Message}");
                _cachedIcons.TryAdd(iconReference, null);
                return null;
            }
        }

        private static bool IsRemoteUrl(string value) =>
            value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        private static bool TryLoadBundledAsset(string iconReference, out Bitmap? bitmap)
        {
            bitmap = null;
            var fileName = ExtractAssetFileName(iconReference);
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            if (!fileName.Equals("Vanilla.png", StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                var uri = new Uri($"avares://SUSModder/Assets/{fileName}");
                using var asset = AssetLoader.Open(uri);
                bitmap = new Bitmap(asset);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ModIconPreloader] Bundled asset load failed for {fileName}: {ex.Message}");
                return false;
            }
        }

        private static string? ExtractAssetFileName(string iconReference)
        {
            if (string.IsNullOrWhiteSpace(iconReference))
                return null;

            if (iconReference.Equals("Vanilla.png", StringComparison.OrdinalIgnoreCase))
                return "Vanilla.png";

            if (IsRemoteUrl(iconReference) &&
                iconReference.Contains("/Vanilla.png", StringComparison.OrdinalIgnoreCase))
            {
                return "Vanilla.png";
            }

            return null;
        }

        public static void ClearCache()
        {
            foreach (var bitmap in _cachedIcons.Values)
            {
                bitmap?.Dispose();
            }
            _cachedIcons.Clear();
        }
    }
}
