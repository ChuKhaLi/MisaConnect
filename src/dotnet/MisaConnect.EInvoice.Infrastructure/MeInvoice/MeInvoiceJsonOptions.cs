using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MisaConnect.EInvoice.Infrastructure.MeInvoice;

internal static class MeInvoiceJsonOptions
{
    /// <summary>Wire profile — PascalCase preserved exactly, nulls omitted, decimals as numbers.</summary>
    public static readonly JsonSerializerOptions Wire = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new DateAsStringConverter(),
        }
    };

    public static readonly JsonSerializerOptions LowerCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed class DateAsStringConverter : JsonConverter<DateOnly>
    {
        public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var s = reader.GetString();
            return DateOnly.ParseExact(s ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
    }
}
