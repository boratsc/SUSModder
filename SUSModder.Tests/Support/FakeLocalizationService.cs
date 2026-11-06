using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using SUSModder.Core.Services.Localization;

namespace SUSModder.Tests.Support;

/// <summary>
/// Fake lokalizacji zwracający sam klucz — testy dowodzą, że UI używa
/// stabilnych kluczy i nigdy nie pokazuje surowego tekstu serwera.
/// </summary>
internal sealed class FakeLocalizationService : ILocalizationService
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentCulture { get; private set; } = "pl";

    public string Get(string key) => key;

    public string GetFormatted(string key, params object[] args) =>
        key + "|" + string.Join(",", args);

    public void ChangeCulture(string culture)
    {
        CurrentCulture = culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCulture)));
    }

    public bool IsCultureAvailable(string culture) => culture is "pl" or "en";

    public IEnumerable<string> GetAvailableCultures() => new[] { "pl", "en" };
}
