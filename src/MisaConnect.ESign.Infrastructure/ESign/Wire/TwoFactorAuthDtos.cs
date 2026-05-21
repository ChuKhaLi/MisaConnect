using System.Text.Json.Serialization;

namespace MisaConnect.ESign.Infrastructure.ESign.Wire;

internal sealed class TwoFactorAuthRequestDto
{
    [JsonPropertyName("userName")]
    public string UserName { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("otpType")]
    public int OtpType { get; set; }

    [JsonPropertyName("remember")]
    public bool Remember { get; set; }
}

internal static class TwoFactorAuthResponseDto
{
    // Type alias semantics: the /two-factor-auth success envelope is identical
    // to /login-api per the MISA doc, so the deserialization target is
    // LoginResponseDto verbatim. Keep this class for future divergence and
    // call-site clarity in MisaESignWireClient.
}
