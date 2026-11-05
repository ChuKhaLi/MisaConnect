using System.Text.Json;
using System.Text.Json.Serialization;

namespace MisaConnect.ESign.Infrastructure.ESign.Wire;

/// <summary>
/// Reads a nullable boolean tolerantly: JSON <c>true</c>/<c>false</c> as is, the strings
/// <c>"true"</c>/<c>"false"</c> (trimmed, case-insensitive) as booleans, and
/// <c>null</c> or any other string or number as <c>null</c>. MISA sends the
/// <c>error</c> flag as a boolean, a string, or <c>null</c> depending on the endpoint.
/// </summary>
internal sealed class LooseNullableBooleanConverter : JsonConverter<bool?>
{
    public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True:
                return true;
            case JsonTokenType.False:
                return false;
            case JsonTokenType.String:
                var text = reader.GetString()?.Trim();
                if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)) return false;
                return null;
            default:
                // Null, numbers and anything else: consumed by the reader's caller for
                // scalar tokens; skip defensively for structured values.
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                {
                    reader.Skip();
                }
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
    {
        if (value is { } b) writer.WriteBooleanValue(b);
        else writer.WriteNullValue();
    }
}
