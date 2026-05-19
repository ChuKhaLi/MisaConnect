using System.Text.Json.Serialization;

namespace MisaConnect.ESign.Infrastructure.ESign.Wire;

internal sealed class RefreshTokenRequestDto
{
    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = string.Empty;
}

/// <summary>
/// Refresh-token response. Per wire-envelopes §E2 the doc shows a flat shape
/// but the live sandbox sometimes returns the login envelope; the implementation
/// tries the envelope first and falls back to flat fields on the same DTO.
/// </summary>
internal sealed class RefreshTokenResponseDto
{
    [JsonPropertyName("status")]
    public LoginStatusBlockDto? Status { get; set; }

    [JsonPropertyName("data")]
    public LoginDataBlockDto? Data { get; set; }

    // Flat-shape fields (top-level).
    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("remoteSigningAccessToken")]
    public string? RemoteSigningAccessToken { get; set; }

    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expiresIn")]
    public int? ExpiresIn { get; set; }
}
