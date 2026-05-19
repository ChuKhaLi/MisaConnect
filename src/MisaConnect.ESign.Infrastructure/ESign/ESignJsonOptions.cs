using System.Text.Json;
using System.Text.Json.Serialization;

namespace MisaConnect.ESign.Infrastructure.ESign;

internal static class ESignJsonOptions
{
    /// <summary>
    /// Wire profile — MISA's eSign DTOs are case-exact per Principle IV.
    /// Casing is enforced via <see cref="JsonPropertyNameAttribute"/> on each DTO.
    /// </summary>
    public static readonly JsonSerializerOptions Wire = new()
    {
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
