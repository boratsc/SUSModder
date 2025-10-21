using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SUSModder.Services
{
    /// <summary>
    /// Serwis do preloadowania i cachowania ikon modów
    /// </summary>
    public class ModIconPreloader
    {
        private static readonly ConcurrentDictionary<string, Bitmap?> _cachedIcons = new();
        private static readonly object _lockObject = new();
        private static bool _isPreloading = false;

        /// <summary>
        /// Wczytuje ikonę z cache lub ładuje ją z zasobów
        /// </summary>
        public static Bitmap? GetIcon(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return null;

            // Sprawdź cache
            if (_cachedIcons.TryGetValue(fileName, out var cachedBitmap))
                return cachedBitmap;

            // Załaduj synchronicznie jeśli nie ma w cache
            return LoadIconSync(fileName);
        }

        /// <summary>
        /// Preloaduje wszystkie ikony modów w tle
        /// </summary>
        public static async Task PreloadIconsAsync(IEnumerable<string?> fileNames)
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

                foreach (var fileName in fileNames)
                {
                    if (string.IsNullOrWhiteSpace(fileName))
                        continue;

                    // Skip jeśli już jest w cache
                    if (_cachedIcons.ContainsKey(fileName))
                        continue;

                    tasks.Add(Task.Run(() => LoadIconSync(fileName)));
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

        private static Bitmap? LoadIconSync(string fileName)
        {
            if (_cachedIcons.ContainsKey(fileName))
                return _cachedIcons[fileName];

            try
            {
                var uri = new Uri($"avares://SUSModder/Assets/{fileName}");
                var asset = AssetLoader.Open(uri);
                var bitmap = new Bitmap(asset);

                _cachedIcons.TryAdd(fileName, bitmap);
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ModIconPreloader] ERROR loading {fileName}: {ex.Message}");
                _cachedIcons.TryAdd(fileName, null);
                return null;
            }
        }

        /// <summary>
        /// Czyści cache ikon
        /// </summary>
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
