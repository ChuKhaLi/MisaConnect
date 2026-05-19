using System.Text.Json.Serialization;

namespace MisaConnect.ESign.Infrastructure.ESign.Wire;

internal sealed class ResponseErrorDto
{
    [JsonPropertyName("error")]
    public bool Error { get; set; }

    [JsonPropertyName("errorCode")]
    [JsonConverter(typeof(LooseStringConverter))]
    public string? ErrorCode { get; set; }

    [JsonPropertyName("devMsg")]
    public string? DevMsg { get; set; }

    [JsonPropertyName("userMsg")]
    public string? UserMsg { get; set; }
}
