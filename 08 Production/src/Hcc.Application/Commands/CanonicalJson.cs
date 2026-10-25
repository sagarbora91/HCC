using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hcc.Application.Commands;

/// <summary>
/// Canonical JSON for idempotency payload hashing (contracts §2): camelCase properties, object keys sorted ordinally,
/// no whitespace, nulls omitted. Two requests with the same meaning hash the same regardless of key order.
/// </summary>
public static class CanonicalJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static JsonSerializerOptions SerializerOptions => Options;

    public static string Serialize<T>(T value)
    {
        var node = JsonSerializer.SerializeToNode(value, Options);
        return Canonicalize(node)?.ToJsonString(Options) ?? "null";
    }

    public static byte[] Sha256<T>(T value) => SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(value)));

    private static JsonNode? Canonicalize(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.Where(p => p.Value is not null)
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, Canonicalize(p.Value)))),
        JsonArray a => new JsonArray(a.Select(Canonicalize).ToArray()),
        null => null,
        _ => node.DeepClone(),
    };
}
