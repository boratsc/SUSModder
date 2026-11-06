using System.Text.Json.Nodes;

namespace SUSModder.Core.Tests.Support;

internal static class CommunityChatTestPayload
{
    // Legacy envelope tests still need complete message snapshots under the new author contract.
    // Contract-negative tests must use raw FakeHttpMessageHandler.Json instead of this fixture helper.
    public static string WithAuthors(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (System.Text.Json.JsonException) { return json; }
        Visit(root);
        return root?.ToJsonString() ?? json;
    }

    private static void Visit(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            foreach (var child in array) Visit(child);
        }
        else if (node is JsonObject obj)
        {
            var names = obj.Select(p => p.Key).ToArray();
            if (names.Any(k => k.Equals("content", StringComparison.OrdinalIgnoreCase)))
            {
                if (!names.Any(k => k.Equals("author", StringComparison.OrdinalIgnoreCase)))
                    obj["author"] = JsonNode.Parse("""{"id":"test-author","nickname":{"pl":"Kosmiczna Kapibara · ABC123","en":"Cosmic Capybara · ABC123"}}""");
                if (!names.Any(k => k.Equals("publishedAt", StringComparison.OrdinalIgnoreCase) ||
                    k.Equals("createdAt", StringComparison.OrdinalIgnoreCase)))
                    obj["publishedAt"] = "2026-10-25T18:00:00Z";
            }
            foreach (var key in names) Visit(obj[key]);
        }
    }

    public static string SuccessfulPost(bool moderationWarning = false) =>
        WithAuthors("""{"data":{"id":"e1","content":"abcdefghij","isOwn":true,"canDelete":true,"moderationWarning":WARNING}}"""
            .Replace("WARNING", moderationWarning ? "true" : "false"));
}
