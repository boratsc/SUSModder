using SUSModder.Core.Models;
using SUSModder.Core.Configuration;
using SUSModder.Core.Diagnostics;
using SUSModder.ViewModels;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using System;

namespace SUSModder.Services
{
    public class DiscordIconPreloader
    {
        private static List<DiscordServerViewModel>? _preloadedServers;
        private static bool _isPreloading = false;
        private static bool _preloadCompleted = false;

        public static async Task PreloadDiscordIconsAsync()
        {
            if (_isPreloading || _preloadCompleted)
                return;

            _isPreloading = true;

            try
            {
                System.Diagnostics.Debug.WriteLine("[DiscordIconPreloader] Starting preload of Discord icons...");

                // Użyj cache'owanej konfiguracji z DI zamiast budowania nowej
                var configuration = App.GetService<IConfiguration>();

                var diagnosticsOutput = new UIDiagnosticsOutput((message) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[Discord Preloader] {message}");
                });

                var discordService = new DiscordFavoritesService(configuration, diagnosticsOutput);
                var serverDataList = await discordService.GetDiscordFavoritesAsync();
                var discordServers = DiscordServerAdapter.FromServerDataList(serverDataList);

                // Konwertuj na ViewModels i załaduj ikony
                var serverViewModels = discordServers.Select(server => new DiscordServerViewModel(server)).ToList();

                var loadTasks = serverViewModels.Select(async serverVM =>
                {
                    await serverVM.LoadIconAsync();
                    return serverVM;
                }).ToArray();

                _preloadedServers = (await Task.WhenAll(loadTasks)).ToList();

                System.Diagnostics.Debug.WriteLine($"[DiscordIconPreloader] Preloaded {_preloadedServers.Count} Discord servers with icons");
                _preloadCompleted = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DiscordIconPreloader] Error during preload: {ex.Message}");
                _preloadedServers = null;
            }
            finally
            {
                _isPreloading = false;
            }
        }

        public static List<DiscordServerViewModel>? GetPreloadedServers()
        {
            return _preloadedServers?.ToList();
        }

        public static bool IsPreloading => _isPreloading;
        public static bool IsPreloadCompleted => _preloadCompleted;
    }
}
