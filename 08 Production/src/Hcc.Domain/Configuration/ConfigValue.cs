using System.Globalization;
using System.Text.Json;

namespace Hcc.Domain.Configuration;

/// <summary>Kind of a configuration value on the wire (JSON): text, integer (paise, bps, minutes…), on/off, null, or anything else (invalid).</summary>
public enum ConfigValueKind
{
    Null,
    Text,
    Number,
    Boolean,
    Other,
}

/// <summary>
/// One configuration value (prototype <c>unknown</c> in <c>s.configX.values</c>): a JSON string, a safe integer, a boolean or null.
/// Stored as its JSON text in <c>cfg.setting_change.value_json</c>. Fractions, objects and arrays are kept as <see cref="ConfigValueKind.Other"/>
/// so validation can refuse them with the setting's own message.
/// </summary>
public sealed record ConfigValue(ConfigValueKind Kind, string? Text = null, long Number = 0, bool Flag = false, string? Raw = null)
{
    public static ConfigValue Null { get; } = new(ConfigValueKind.Null);

    public static ConfigValue Of(string text) => new(ConfigValueKind.Text, Text: text);

    public static ConfigValue Of(long number) => new(ConfigValueKind.Number, Number: number);

    public static ConfigValue Of(bool flag) => new(ConfigValueKind.Boolean, Flag: flag);

    public bool IsNull => Kind == ConfigValueKind.Null;

    /// <summary>JavaScript truthiness (prototype <c>v?…:…</c> for secrets): non-empty text, non-zero number, true.</summary>
    public bool IsSet => Kind switch
    {
        ConfigValueKind.Text => !string.IsNullOrEmpty(Text),
        ConfigValueKind.Number => Number != 0,
        ConfigValueKind.Boolean => Flag,
        ConfigValueKind.Other => true,
        _ => false,
    };

    public static ConfigValue FromJson(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => Of(e.GetString() ?? string.Empty),
        JsonValueKind.Number => e.TryGetInt64(out var n) && Math.Abs(n) <= 9_007_199_254_740_991L ? Of(n) : new ConfigValue(ConfigValueKind.Other, Raw: e.GetRawText()),
        JsonValueKind.True => Of(true),
        JsonValueKind.False => Of(false),
        JsonValueKind.Null or JsonValueKind.Undefined => Null,
        _ => new ConfigValue(ConfigValueKind.Other, Raw: e.GetRawText()),
    };

    public static ConfigValue FromJson(JsonElement? e) => e is { } x ? FromJson(x) : Null;

    public static ConfigValue Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return FromJson(doc.RootElement);
    }

    public string ToJson() => Kind switch
    {
        ConfigValueKind.Text => JsonSerializer.Serialize(Text ?? string.Empty),
        ConfigValueKind.Number => Number.ToString(CultureInfo.InvariantCulture),
        ConfigValueKind.Boolean => Flag ? "true" : "false",
        ConfigValueKind.Other => Raw ?? "null",
        _ => "null",
    };

    public JsonElement ToElement()
    {
        using var doc = JsonDocument.Parse(ToJson());
        return doc.RootElement.Clone();
    }

    /// <summary>Prototype <c>String(v)</c>.</summary>
    public override string ToString() => Kind switch
    {
        ConfigValueKind.Text => Text ?? string.Empty,
        ConfigValueKind.Number => Number.ToString(CultureInfo.InvariantCulture),
        ConfigValueKind.Boolean => Flag ? "true" : "false",
        ConfigValueKind.Other => Raw ?? string.Empty,
        _ => string.Empty,
    };
}
