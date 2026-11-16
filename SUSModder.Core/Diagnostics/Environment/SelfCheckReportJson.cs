using System.Text.Json;
using System.Text.Json.Serialization;

namespace SUSModder.Core.Diagnostics.Environment;

public static class SelfCheckReportJson
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, IncludeFields = true, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    public static string Serialize(SelfCheckReport report) => JsonSerializer.Serialize(report, Options);
    public static SelfCheckReport? Deserialize(string json) => JsonSerializer.Deserialize<SelfCheckReport>(json, Options);
}
