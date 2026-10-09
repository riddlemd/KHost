using System.Text.Json;
using System.Text.Json.Nodes;

namespace KHost.CatalogSync;

/// <summary>Carries a catalog file's <c>$schema</c> line across a rewrite.</summary>
/// <remarks>The host's model has no such property and ignores it on read, so a plain
/// deserialise-and-write would drop the line editors validate against.</remarks>
internal static class CatalogSchemaLine
{
    /// <summary>The top-level <c>$schema</c> string in <paramref name="json"/>; null when there is none
    /// or the text does not parse.</summary>
    internal static string? Read(string json)
    {
        try
        {
            return JsonNode.Parse(json) is JsonObject root && root["$schema"] is JsonValue value
                && value.TryGetValue<string>(out var schema)
                ? schema
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary><paramref name="serialized"/> with <paramref name="schema"/> as its first property, or
    /// unchanged when <paramref name="schema"/> is null.</summary>
    internal static string Prepend(string serialized, string? schema, JsonSerializerOptions options)
    {
        if (schema is null)
            return serialized;

        var original = JsonNode.Parse(serialized)!.AsObject();
        var withSchema = new JsonObject { ["$schema"] = schema };

        foreach (var (name, value) in original.ToList())
        {
            original.Remove(name);
            withSchema[name] = value;
        }

        return withSchema.ToJsonString(options) + Environment.NewLine;
    }
}
