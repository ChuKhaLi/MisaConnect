using System.Text.Json.Serialization;

namespace MisaConnect.ESign.Infrastructure.ESign.Wire;

internal sealed class ResendOtpRequestDto
{
    [JsonPropertyName("userName")]
    public string UserName { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;
}

internal sealed class ResendOtpResponseDto
{
    [JsonPropertyName("status")]
    public LoginStatusBlockDto? Status { get; set; }

    [JsonPropertyName("data")]
    public ResendOtpDataDto? Data { get; set; }
}

internal sealed class ResendOtpDataDto
{
    [JsonPropertyName("user")]
    public ResendOtpUserDto? User { get; set; }
}

internal sealed class ResendOtpUserDto
{
    [JsonPropertyName("username")]
    public string? Username { get; set; }
}
