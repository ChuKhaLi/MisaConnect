using System.Text.Json.Serialization;

namespace MisaConnect.ESign.Infrastructure.ESign.Wire;

internal sealed class CertificateDto
{
    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    [JsonPropertyName("keyAlias")]
    public string? KeyAlias { get; set; }

    [JsonPropertyName("appName")]
    public string? AppName { get; set; }

    [JsonPropertyName("keyStatus")]
    public string? KeyStatus { get; set; }

    [JsonPropertyName("certificate")]
    public string? Certificate { get; set; }

    // NOTE: MISA's published doc spells this "certiticateChain" (typo).
    // Preserved verbatim per Constitution Principle IV.
    [JsonPropertyName("certiticateChain")]
    public List<string>? CertificateChain { get; set; }

    [JsonPropertyName("certStatus")]
    public string? CertStatus { get; set; }

    [JsonPropertyName("effectiveDate")]
    public DateTimeOffset? EffectiveDate { get; set; }

    [JsonPropertyName("expirationDate")]
    public DateTimeOffset? ExpirationDate { get; set; }

    [JsonPropertyName("emailName")]
    public string? EmailName { get; set; }

    [JsonPropertyName("isAutoSign")]
    public bool IsAutoSign { get; set; }
}
