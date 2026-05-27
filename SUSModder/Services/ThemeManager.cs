using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using SUSModder.Core.Services;
using SUSModder.Core.Configuration;

namespace SUSModder.Services
{
    /// <summary>
    /// Zarządza motywami aplikacji (Dark, Light, Pink).
    /// Motyw przechowywany w user_settings (SQLite/JSON), nie w appsettings.json.
    /// </summary>
    public class ThemeManager
    {
        private readonly Uri _darkThemeUri = new Uri("avares://SUSModder/Themes/DarkTheme.axaml");
        private readonly Uri _lightThemeUri = new Uri("avares://SUSModder/Themes/LightTheme.axaml");
        private readonly Uri _pinkThemeUri = new Uri("avares://SUSModder/Themes/PinkTheme.axaml");
        
        private ResourceDictionary? _currentThemeDictionary;
        private readonly UserSettingsService _userSettingsService;

        public enum ThemeType
        {
            Dark,
            Light,
            Pink
        }

        public ThemeManager() : this(new UserSettingsService())
        {
        }

        public ThemeManager(UserSettingsService userSettingsService)
        {
            _userSettingsService = userSettingsService;
        }

        /// <summary>
        /// Wczytuje zapisany motyw z user_settings.
        /// </summary>
        public ThemeType LoadSavedTheme()
        {
            try
            {
                var userSettings = _userSettingsService.LoadUserSettings();
                var savedTheme = userSettings.Theme;
                var theme = savedTheme switch
                {
                    "light" => ThemeType.Light,
                    "pink" => ThemeType.Pink,
                    _ => ThemeType.Dark
                };
                System.Diagnostics.Debug.WriteLine($"Wczytano motyw: {savedTheme} -> {theme}");
                return theme;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd wczytywania motywu: {ex.Message}");
                return ThemeType.Dark;
            }
        }

        public ThemeType ToggleTheme(ThemeType currentTheme)
        {
            var newTheme = currentTheme switch
            {
                ThemeType.Dark => ThemeType.Light,
                ThemeType.Light => ThemeType.Pink,
                ThemeType.Pink => ThemeType.Dark,
                _ => ThemeType.Dark
            };
            SaveTheme(newTheme);
            return newTheme;
        }

        /// <summary>
        /// Zapisuje wybrany motyw do user_settings.
        /// </summary>
        public void SaveTheme(ThemeType theme)
        {
            try
            {
                var themeValue = theme switch
                {
                    ThemeType.Light => "light",
                    ThemeType.Pink => "pink",
                    _ => "dark"
                };
                _userSettingsService.UpdateUserSetting(settings => settings.Theme = themeValue);
                System.Diagnostics.Debug.WriteLine($"Zapisano motyw: {themeValue}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd zapisywania motywu: {ex.Message}");
            }
        }

        /// <summary>
        /// Aplikuje wybrany motyw do aplikacji
        /// </summary>
        public void ApplyTheme(ThemeType theme)
        {
            try
            {
                if (Application.Current == null)
                    return;

                // Usuń poprzedni słownik jeśli był załadowany
                if (_currentThemeDictionary != null)
                    Application.Current.Resources.MergedDictionaries.Remove(_currentThemeDictionary);

                var uri = theme switch
                {
                    ThemeType.Light => _lightThemeUri,
                    ThemeType.Pink => _pinkThemeUri,
                    _ => _darkThemeUri
                };

                var loaded = AvaloniaXamlLoader.Load(uri);

                if (loaded is ResourceDictionary newDict)
                {
                    Application.Current.Resources.MergedDictionaries.Add(newDict);
                    _currentThemeDictionary = newDict;
                }

                // Ustaw również systemowy motyw dla kompatybilności
                var systemTheme = theme switch
                {
                    ThemeType.Light => ThemeVariant.Light,
                    ThemeType.Pink => ThemeVariant.Light, // Różowy bazuje na jasnym
                    _ => ThemeVariant.Dark
                };

                Application.Current.RequestedThemeVariant = systemTheme;

                System.Diagnostics.Debug.WriteLine($"Applied theme: {theme}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying theme: {ex.Message}");
                // Fallback - użyj domyślnego motywu
                if (Application.Current != null)
                {
                    Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
                }
            }
        }

        /// <summary>
        /// Zwraca tekst dla przycisku zmiany motywu
        /// </summary>
        public string GetThemeButtonText(ThemeType theme) => theme switch
        {
            ThemeType.Dark => "Motyw jasny",
            ThemeType.Light => "Motyw różowy",
            ThemeType.Pink => "Motyw ciemny",
            _ => "Motyw ciemny"
        };

        /// <summary>
        /// Zwraca ikonę dla przycisku zmiany motywu
        /// </summary>
        public string GetThemeButtonIcon(ThemeType theme) => theme switch
        {
            ThemeType.Dark => "☀️",
            ThemeType.Light => "💖",
            ThemeType.Pink => "🌙",
            _ => "🌙"
        };
    }
}
