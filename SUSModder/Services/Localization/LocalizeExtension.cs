using System;
using System.ComponentModel;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using SUSModder.Core.Services.Localization;

namespace SUSModder.Services.Localization;

/// <summary>
/// MarkupExtension umożliwiająca użycie {local:Localize Key} w AXAML.
/// </summary>
public class LocalizeExtension : MarkupExtension
{
    public string Key { get; set; }

    public LocalizeExtension()
    {
        Key = string.Empty;
    }

    public LocalizeExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // Pobierz LocalizationService z DI
        var locService = App.GetService<ILocalizationService>();

        if (locService == null)
            return $"[LOC_SERVICE_NOT_FOUND: {Key}]";

        // Stwórz wrapper który reaguje na zmiany
        var wrapper = new LocalizedBinding(locService, Key);

        // Zwróć binding do właściwości Value wrappera
        return new Avalonia.Data.Binding
        {
            Source = wrapper,
            Path = nameof(LocalizedBinding.Value),
            Mode = BindingMode.OneWay
        };
    }
}

/// <summary>
/// Helper class który reaguje na zmianę języka i automatycznie aktualizuje wartość.
/// </summary>
public class LocalizedBinding : INotifyPropertyChanged
{
    private readonly ILocalizationService _locService;
    private readonly string _key;

    public string Value => _locService.Get(_key);

    public event PropertyChangedEventHandler? PropertyChanged;

    public LocalizedBinding(ILocalizationService locService, string key)
    {
        _locService = locService;
        _key = key;

        // Nasłuchuj zmiany CurrentCulture
        _locService.PropertyChanged += (s, e) =>
        {
            // Gdy zmieni się CurrentCulture lub wszystkie właściwości
            if (e.PropertyName == nameof(ILocalizationService.CurrentCulture) || string.IsNullOrEmpty(e.PropertyName))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        };
    }
}
