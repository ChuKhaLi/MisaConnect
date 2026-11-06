using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MisaConnect.ESign.Infrastructure.ESign.Wire;

/// <summary>
/// Reads an optional integer leniently, so an odd value never fails a login: a JSON integer
/// as-is; a string of an optional leading <c>-</c> and digits parsed (invariant culture);
/// anything else (a non-integer or out-of-range number, any other string, a boolean, an object
/// or an array) reads as <see langword="null"/>, i.e. "missing".
/// </summary>
internal sealed class LenientInt32Converter : JsonConverter<int?>
{
    public override bool HandleNull => true;

    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetInt32(out var number) ? number : null;
            case JsonTokenType.String:
                // Only an optional leading '-' (not '+'), matching the TS rule /^-?\d+$/.
                var text = reader.GetString()?.Trim();
                return text is { Length: > 0 } && text[0] != '+'
                    && int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
            case JsonTokenType.StartObject:
            case JsonTokenType.StartArray:
                reader.Skip();
                return null;
            default:
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value is { } v)
        {
            writer.WriteNumberValue(v);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
