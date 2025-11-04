using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using SUSModder.Core.Configuration;

namespace SUSModder.ViewModels
{
    /// <summary>
    /// Partial class zawierający zarządzanie motywami aplikacji
    /// </summary>
    public partial class MainWindowViewModel
    {
        private void LoadSavedTheme()
        {
            try
            {
                var userSettings = _userSettingsService.LoadUserSettings();
                _currentTheme = userSettings.Theme switch
                {
                    "light" => ThemeType.Light,
                    "pink" => ThemeType.Pink,
                    _ => ThemeType.Dark
                };
                System.Diagnostics.Debug.WriteLine($"Wczytano motyw: {userSettings.Theme} -> {_currentTheme}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Błąd wczytywania motywu: {ex.Message}");
                _currentTheme = ThemeType.Dark; // domyślnie ciemny
            }
        }

        private void ToggleTheme()
        {
            // Przełącz między trzema motywami cyklicznie
            CurrentTheme = CurrentTheme switch
            {
                ThemeType.Dark => ThemeType.Light,
                ThemeType.Light => ThemeType.Pink,
                ThemeType.Pink => ThemeType.Dark,
                _ => ThemeType.Dark
            };

            // Zapisz nowy motyw
            try
            {
                var themeValue = CurrentTheme switch
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

        private void ApplyTheme(ThemeType theme)
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
    }
}
