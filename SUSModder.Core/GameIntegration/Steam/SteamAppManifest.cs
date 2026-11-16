using System.Globalization;

namespace SUSModder.Core.GameIntegration.Steam;

public sealed class SteamAppManifest
{
    public string? AppId { get; init; }
    public string? InstallDir { get; init; }
    public int? StateFlags { get; init; }
    public string? UserConfigBetaKey { get; init; }
    public string? MountedConfigBetaKey { get; init; }
    public bool IsFullyInstalled => StateFlags.HasValue && (StateFlags.Value & 4) != 0;

    public static SteamAppManifest? TryRead(string path)
    {
        try { return TryParse(File.ReadAllText(path)); }
        catch { return null; }
    }

    public static SteamAppManifest? TryParse(string content)
    {
        try
        {
            var tokens = Tokenize(content);
            var index = 0;
            var root = ParseBody(tokens, ref index);
            if (root is null || index != tokens.Count) return null;
            var app = GetObject(root, "AppState");
            if (app is null) return null;
            int? flags = int.TryParse(GetValue(app, "StateFlags"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
            return new SteamAppManifest
            {
                AppId = GetValue(app, "appid"), InstallDir = GetValue(app, "installdir"), StateFlags = flags,
                UserConfigBetaKey = GetValue(GetObject(app, "UserConfig"), "betakey"),
                MountedConfigBetaKey = GetValue(GetObject(app, "MountedConfig"), "betakey")
            };
        }
        catch { return null; }
    }

    private static string? GetValue(Dictionary<string, object>? obj, string key) =>
        obj != null && obj.TryGetValue(key, out var value) ? value as string : null;
    private static Dictionary<string, object>? GetObject(Dictionary<string, object>? obj, string key) =>
        obj != null && obj.TryGetValue(key, out var value) ? value as Dictionary<string, object> : null;

    private static List<string> Tokenize(string text)
    {
        var result = new List<string>();
        for (int i = 0; i < text.Length;)
        {
            if (char.IsWhiteSpace(text[i])) { i++; continue; }
            if (text[i] == '{' || text[i] == '}') { result.Add(text[i++].ToString()); continue; }
            if (text[i++] != '"') throw new FormatException();
            var value = new System.Text.StringBuilder();
            bool closed = false;
            while (i < text.Length)
            {
                var c = text[i++];
                if (c == '"') { closed = true; break; }
                if (c == '\\')
                {
                    if (i == text.Length) throw new FormatException();
                    var next = text[i++];
                    value.Append(next is '\\' or '"' ? next : '\\').Append(next is '\\' or '"' ? "" : next.ToString());
                }
                else value.Append(c);
            }
            if (!closed) throw new FormatException();
            result.Add(value.ToString());
        }
        return result;
    }

    private static Dictionary<string, object>? ParseBody(List<string> tokens, ref int i)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        while (i < tokens.Count)
        {
            if (tokens[i] == "}") return result;
            var key = tokens[i++];
            if (i >= tokens.Count) return null;
            object value;
            if (tokens[i] == "{")
            {
                var nested = ParseObject(tokens, ref i);
                if (nested is null) return null;
                value = nested;
            }
            else if (tokens[i] == "}") return null;
            else value = tokens[i++];
            result[key] = value;
        }
        return result;
    }

    private static Dictionary<string, object>? ParseObject(List<string> tokens, ref int i)
    {
        if (i >= tokens.Count || tokens[i] != "{") return null;
        i++;
        var body = ParseBody(tokens, ref i);
        if (body is null) return null;
        if (i >= tokens.Count || tokens[i] != "}") return null;
        i++;
        return body;
    }
}
