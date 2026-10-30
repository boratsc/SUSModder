using System.Text.Json;
using System.Text.Json.Serialization;
using SUSModder.Core.Api.Models;

namespace SUSModder.Core.Api.Json;

/// <summary>
/// Wspólne wsparcie dla konwerterów obecności członów JSON (3.1-04).
/// Konwertery są rejestrowane wyłącznie w opcjach klienta API — dzięki temu
/// wewnętrzne odczytanie nie wywołuje konwertera ponownie (brak rekurencji),
/// a kody deserializujące poza klientem API zachowują dotychczasowe zachowanie.
/// </summary>
internal static class CatalogPresenceSupport
{
    /// <summary>
    /// Opcje odczytu bez konwerterów obecności — muszą odpowiadać opcjom klienta API
    /// (nazwy właściwości DTO są zapisane jawnie, więc polityka nazewnictwa nie ma znaczenia).
    /// </summary>
    internal static readonly JsonSerializerOptions Inner = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    /// <summary>Czyta obiekt JSON i zwraca jego kopię (dokument zostaje zwolniony).</summary>
    internal static JsonElement ReadElement(ref Utf8JsonReader reader)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.Clone();
    }
}

/// <summary>
/// Zapisuje informację o obecności członu <c>icon</c> w elemencie katalogu.
/// Referencja obiektu pozostaje nietknięta (null = brak referencji), dodawane jest
/// wyłącznie flaga obecności członu.
/// </summary>
internal sealed class CatalogItemDtoPresenceConverter : JsonConverter<CatalogItemDto>
{
    public override CatalogItemDto Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var element = CatalogPresenceSupport.ReadElement(ref reader);
        var item = JsonSerializer.Deserialize<CatalogItemDto>(element.GetRawText(), CatalogPresenceSupport.Inner)
                   ?? new CatalogItemDto();
        item.IconMemberPresent = element.TryGetProperty("icon", out _);
        return item;
    }

    public override void Write(Utf8JsonWriter writer, CatalogItemDto value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, CatalogPresenceSupport.Inner);
}

/// <summary>
/// Zapisuje informację o obecności członów <c>vanilla</c> i <c>dll</c> w globalnych
/// ikonach metadanych katalogu. Jawne null pozostaje null — flaga obecności pozwala
/// odróżnić „pole nieobecne” od „jawnie usunięte przypisanie”.
/// </summary>
internal sealed class CatalogGlobalIconsDtoPresenceConverter : JsonConverter<CatalogGlobalIconsDto>
{
    public override CatalogGlobalIconsDto Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var element = CatalogPresenceSupport.ReadElement(ref reader);
        var icons = JsonSerializer.Deserialize<CatalogGlobalIconsDto>(element.GetRawText(), CatalogPresenceSupport.Inner)
                    ?? new CatalogGlobalIconsDto();
        icons.VanillaMemberPresent = element.TryGetProperty("vanilla", out _);
        icons.DllMemberPresent = element.TryGetProperty("dll", out _);
        return icons;
    }

    public override void Write(Utf8JsonWriter writer, CatalogGlobalIconsDto value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, CatalogPresenceSupport.Inner);
}
